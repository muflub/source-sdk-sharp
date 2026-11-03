using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapFormats.Nav;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// Writes a real <c>.nav3d</c> with map-tools' own <see cref="Nav3dWriter"/>
/// from a few axis-aligned solids (plan_hl2mp_ai §8.2): the fixtures the
/// derivation and the refusal facts read through the real reader.
/// <list type="bullet">
/// <item>Voxels are 16 units in 256-unit cells; a voxel is solid when it
/// overlaps a solid box. Each column's free runs are its leaves.</item>
/// <item>A leaf's clearance record (map-tools format §5) holds a corner per
/// solid not wholly below it: the Chebyshev gap from the column's footprint
/// to the box (no width when it reaches into the footprint) and the box's
/// bottom; dominated corners are dropped. A dynamic obstacle's box gives
/// dynamic corners and stays open space.</item>
/// <item>A leaf is grounded and walkable when the voxel under it is solid; its
/// floor is the highest solid top under it. Ladder and water boxes flag the
/// leaves they overlap.</item>
/// <item>Jump links follow map-tools' linker (LevelNavLinker.FindJumps): two
/// walkable floors up to six columns apart along an axis, within the jump
/// height, not a step apart side by side, open at the higher floor, with no
/// floor between within a step of the jump's span.</item>
/// </list>
/// </summary>
internal sealed class VoxelNavFixture(int cellsX, int cellsY, int heightVoxels)
{
    public const float Voxel = 16f;
    private const int CellVoxels = 16;

    private readonly List<Box> _solids = new();
    private readonly List<(Box Box, string Name, string ClassName, Nav3dObstacleKind Kind)> _obstacles = new();
    private readonly List<Box> _ladders = new();
    private readonly List<Box> _water = new();

    public readonly record struct Box(float X0, float Y0, float Z0, float X1, float Y1, float Z1)
    {
        public bool Overlaps(float x0, float y0, float z0, float x1, float y1, float z1)
            => X0 < x1 && x0 < X1 && Y0 < y1 && y0 < Y1 && Z0 < z1 && z0 < Z1;
    }

    public Guid LevelId { get; set; }

    /// <summary>The solid boxes, as a world built from them answers.</summary>
    public IReadOnlyList<Box> Solids => _solids;

    public Nav3dBspBinding? Bsp { get; set; }

    public VoxelNavFixture Solid(float x0, float y0, float z0, float x1, float y1, float z1)
    {
        _solids.Add(new Box(x0, y0, z0, x1, y1, z1));
        return this;
    }

    public VoxelNavFixture Obstacle(string name, string className, Nav3dObstacleKind kind, float x0, float y0, float z0, float x1, float y1, float z1)
    {
        _obstacles.Add((new Box(x0, y0, z0, x1, y1, z1), name, className, kind));
        return this;
    }

    public VoxelNavFixture Ladder(float x0, float y0, float z0, float x1, float y1, float z1)
    {
        _ladders.Add(new Box(x0, y0, z0, x1, y1, z1));
        return this;
    }

    public VoxelNavFixture Water(float x0, float y0, float z0, float x1, float y1, float z1)
    {
        _water.Add(new Box(x0, y0, z0, x1, y1, z1));
        return this;
    }

    public byte[] Write() => Nav3dWriter.Write(Level());

    public Nav3dReader Open() => Nav3dReader.Open(Write());

    public Nav3dLevel Level()
    {
        int gw = cellsX * CellVoxels, gh = cellsY * CellVoxels;
        var leaves = new List<Nav3dLeaf>();
        var starts = new List<uint> { 0 };
        var clearance = new List<byte>();
        var records = new Dictionary<string, uint>();
        var columnOf = new List<(int Gx, int Gy)>();
        for (int cell = 0; cell < cellsX * cellsY; cell++)
        {
            int cx = cell % cellsX, cy = cell / cellsX;
            for (int y = 0; y < CellVoxels; y++)
            {
                for (int x = 0; x < CellVoxels; x++)
                {
                    int gx = cx * CellVoxels + x, gy = cy * CellVoxels + y;
                    foreach ((int lo, int hi) in Runs(gx, gy))
                    {
                        byte[] record = Record(gx, gy, lo);
                        string key = Convert.ToHexString(record);
                        if (!records.TryGetValue(key, out uint offset))
                        {
                            offset = (uint)clearance.Count;
                            records[key] = offset;
                            clearance.AddRange(record);
                        }

                        leaves.Add(Leaf(gx, gy, lo, hi, offset));
                        columnOf.Add((gx, gy));
                    }

                    starts.Add((uint)leaves.Count);
                }
            }
        }

        return new Nav3dLevel
        {
            CellSize = CellVoxels * Voxel,
            VoxelSize = Voxel,
            CellVoxels = CellVoxels,
            CellHeights = heightVoxels == CellVoxels ? null : [.. Enumerable.Repeat(heightVoxels, cellsX * cellsY)],
            Columns = cellsX,
            Rows = cellsY,
            Origin = new Vec3(0, 0, 0),
            FloorNormalZ = 0.7f,
            StepHeight = 18,
            JumpHeight = 56,
            JumpDistance = 100,
            Cells = [.. Enumerable.Range(0, cellsX * cellsY).Select(_ => new Nav3dCell(null, 0, Nav3dRoomRole.None, 0, 0))],
            Presets = [new Nav3dPreset("standing", 32, 72, Nav3dClipClass.Player)],
            Roots = [.. Enumerable.Range(0, cellsX * cellsY).Select(c => c * CellVoxels * CellVoxels)],
            ColumnStarts = [.. starts],
            Leaves = [.. leaves],
            Clearance = [.. clearance],
            Obstacles = [.. _obstacles.Select(o => new Nav3dObstacle(o.Name, o.ClassName, 0, -1, o.Kind,
                                                                      new Vec3(o.Box.X0, o.Box.Y0, o.Box.Z0), new Vec3(o.Box.X1, o.Box.Y1, o.Box.Z1)))],
            Jumps = Jumps(leaves, columnOf, starts, gw, gh),
            LevelId = LevelId,
            Bsp = Bsp,
        };
    }

    private bool SolidAt(int gx, int gy, int z)
        => z < 0 || _solids.Exists(s => s.Overlaps(gx * Voxel, gy * Voxel, z * Voxel, (gx + 1) * Voxel, (gy + 1) * Voxel, (z + 1) * Voxel));

    private IEnumerable<(int Lo, int Hi)> Runs(int gx, int gy)
    {
        int z = 0;
        while (z < heightVoxels)
        {
            if (SolidAt(gx, gy, z))
            {
                z++;
                continue;
            }

            int lo = z;
            while (z < heightVoxels && !SolidAt(gx, gy, z))
                z++;
            yield return (lo, z - 1);
        }
    }

    private static bool Touches(Box b, float x0, float y0, float z0, float x1, float y1, float z1) => b.Overlaps(x0, y0, z0, x1, y1, z1);

    private Nav3dLeaf Leaf(int gx, int gy, int lo, int hi, uint record)
    {
        float x0 = gx * Voxel, y0 = gy * Voxel, z0 = lo * Voxel, x1 = x0 + Voxel, y1 = y0 + Voxel, z1 = (hi + 1) * Voxel;
        bool grounded = lo > 0 && SolidAt(gx, gy, lo - 1);
        float floor = 0f;
        if (grounded)
        {
            floor = _solids.Where(s => s.Overlaps(x0, y0, z0 - Voxel, x1, y1, z0)).Max(s => s.Z1);
        }

        var flags = Nav3dLeafFlags.None;
        if (grounded)
            flags |= Nav3dLeafFlags.GroundedPlayer | Nav3dLeafFlags.GroundedNpc | Nav3dLeafFlags.WalkablePlayer | Nav3dLeafFlags.WalkableNpc;
        bool ladder = _ladders.Exists(b => Touches(b, x0, y0, z0, x1, y1, z1));
        bool water = _water.Exists(b => Touches(b, x0, y0, z0, x1, y1, z1));
        if (ladder)
            flags |= Nav3dLeafFlags.Ladder;
        if (water)
            flags |= Nav3dLeafFlags.Water;
        ushort cost = (ushort)(256 * (water ? 2f : 1f) * (ladder ? 1.5f : 1f));
        return new Nav3dLeaf((byte)lo, (byte)(hi - lo + 1), flags, cost, record, record, floor, floor);
    }

    private byte[] Record(int gx, int gy, int lo)
    {
        float x0 = gx * Voxel, y0 = gy * Voxel, x1 = x0 + Voxel, y1 = y0 + Voxel, bottom = lo * Voxel;
        Nav3dCorner? CornerOf(Box s)
        {
            if (s.Z1 <= bottom)
                return null;
            float gap = MathF.Max(MathF.Max(s.X0 - x1, x0 - s.X1), MathF.Max(s.Y0 - y1, y0 - s.Y1));
            if (gap > 256f)
                return null;
            return new Nav3dCorner(gap < 0f ? float.NegativeInfinity : gap, s.Z0);
        }

        var statics = _solids.Select(CornerOf).OfType<Nav3dCorner>().ToList();
        var kept = new List<Nav3dCorner>();
        float lowest = float.PositiveInfinity;
        foreach (Nav3dCorner c in statics.OrderBy(c => c.Width).ThenBy(c => c.Top))
        {
            if (c.Top < lowest && (kept.Count == 0 || c.Width > kept[^1].Width))
            {
                kept.Add(c);
                lowest = c.Top;
            }
        }

        var dynamics = new List<Nav3dDynamicCorner>();
        for (int o = 0; o < _obstacles.Count; o++)
        {
            if (CornerOf(_obstacles[o].Box) is { } c)
                dynamics.Add(new Nav3dDynamicCorner(o, c));
        }

        return Nav3dClearance.Encode([.. kept], [.. dynamics], []);
    }

    private static Nav3dJump[] Jumps(List<Nav3dLeaf> leaves, List<(int Gx, int Gy)> columnOf, List<uint> starts, int gw, int gh)
    {
        int Column(int gx, int gy)
        {
            if (gx < 0 || gy < 0 || gx >= gw || gy >= gh)
                return -1;
            int cell = (gy / CellVoxels) * (gw / CellVoxels) + gx / CellVoxels;
            return cell * CellVoxels * CellVoxels + (gy % CellVoxels) * CellVoxels + gx % CellVoxels;
        }

        bool Open(int column, int from, int to)
        {
            for (int z = Math.Min(from, to); z <= Math.Max(from, to); z++)
            {
                bool free = false;
                for (int l = (int)starts[column]; l < starts[column + 1] && !free; l++)
                    free = z >= leaves[l].ZLo && z <= leaves[l].ZHi;
                if (!free)
                    return false;
            }

            return true;
        }

        bool FloorNear(int column, float lo, float hi)
        {
            for (int l = (int)starts[column]; l < starts[column + 1]; l++)
            {
                if (leaves[l].IsWalkable(Nav3dClipClass.Player) && leaves[l].PlayerFloorZ >= lo && leaves[l].PlayerFloorZ <= hi)
                    return true;
            }

            return false;
        }

        var found = new SortedDictionary<(uint A, uint B, float Rise), (Nav3dDirection Direction, byte Columns)>();
        for (int leaf = 0; leaf < leaves.Count; leaf++)
        {
            Nav3dLeaf from = leaves[leaf];
            if (!from.IsWalkable(Nav3dClipClass.Player))
                continue;
            (int gx, int gy) = columnOf[leaf];
            float fa = from.PlayerFloorZ;
            for (int d = 0; d < 4; d++)
            {
                (int dx, int dy) = d switch { 0 => (1, 0), 1 => (0, 1), 2 => (-1, 0), _ => (0, -1) };
                for (int k = 1; k <= 6; k++)
                {
                    int column = Column(gx + k * dx, gy + k * dy);
                    if (column < 0)
                        break;
                    for (int other = (int)starts[column]; other < starts[column + 1]; other++)
                    {
                        Nav3dLeaf to = leaves[other];
                        if (!to.IsWalkable(Nav3dClipClass.Player))
                            continue;
                        float rise = to.PlayerFloorZ - fa;
                        if (MathF.Abs(rise) > 56f || (k == 1 && MathF.Abs(rise) <= 18f))
                            continue;
                        int top = fa >= to.PlayerFloorZ ? from.ZLo : to.ZLo;
                        if (!Open(Column(gx, gy), from.ZLo, top) || !Open(column, to.ZLo, top))
                            continue;
                        bool gap = true;
                        for (int j = 1; j < k && gap; j++)
                        {
                            int between = Column(gx + j * dx, gy + j * dy);
                            gap = between >= 0 && Open(between, top, top)
                                  && !FloorNear(between, MathF.Min(fa, to.PlayerFloorZ) - 18f, MathF.Max(fa, to.PlayerFloorZ) + 18f);
                        }

                        if (!gap)
                            continue;
                        (uint a, uint b, float r, Nav3dDirection dir) = leaf < other
                            ? ((uint)leaf, (uint)other, rise, (Nav3dDirection)d)
                            : ((uint)other, (uint)leaf, -rise, (Nav3dDirection)((d + 2) % 4));
                        found.TryAdd((a, b, r), (dir, (byte)k));
                    }
                }
            }
        }

        return [.. found.Select(f => new Nav3dJump(f.Key.A, f.Key.B, f.Key.Rise, 1, f.Value.Direction, f.Value.Columns))];
    }
}
