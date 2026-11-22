using SourceSharp.Ai;
using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapFormats.Nav;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Director;
using TF2.Descent.Server.Horde.Nav;
using TF2.Descent.Ai.Level;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;
using static TF2.Descent.Tests.Horde.HordeNavDataTests;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The hulls' floors on depth 1 seed 1 (<see cref="NavHullReach"/>): a giant of scale 1.6 and up is
/// cut off in rooms 6, 16 and 17 (<see cref="HordeNavDataTests"/>), so the spawn director never
/// posts one there; a smaller robot is posted there as before.
/// </summary>
public sealed class NavHullReachTests
{
    private static AiVector Middle(int room)
    {
        var r = FixtureLevel.Rooms[room];
        foreach (var m in r.Markers)
            return m.Position;
        return (r.Min + r.Max) * 0.5f;
    }

    [Fact]
    public void A_giant_in_a_cut_off_room_does_not_join_and_one_at_the_arrival_does()
    {
        var reach = new NavHullReach(Nav);

        Assert.True(reach.Joins(FixtureLevel.RoomAt(Arrival), Arrival, 1.75));
        foreach (int room in new[] { 6, 16, 17 })
        {
            Assert.False(reach.Joins(room, Middle(room), 1.75), $"a giant of 1.75 joins from room {room}");
            Assert.False(reach.Joins(room, Middle(room), 1.9), $"a giant of 1.9 joins from room {room}");
            Assert.True(reach.Joins(room, Middle(room), 1.5), $"a giant of 1.5 does not join from room {room}");
            Assert.True(reach.Joins(room, Middle(room), 1.0), $"an ordinary robot does not join from room {room}");
        }
    }

    /// <summary>
    /// The refusal on a floor cut where nothing rests on a voxel (<see cref="CutFloorLevel"/>): a
    /// giant of 1.75 stands beyond the corridor (its class holds a cell there) and on the near side,
    /// and is refused only beyond it, whose floor its hull does not share with the near room's.
    /// </summary>
    [Fact]
    public void A_giant_beyond_a_corridor_narrower_than_its_hull_does_not_join()
    {
        var reach = new NavHullReach(CutFloorLevel.Nav);
        Assert.True(CutFloorLevel.Stands(CutFloorLevel.Far, ClearanceClass.Hull4), "the giant's class holds no cell beyond the corridor");
        Assert.True(reach.Joins(0, CutFloorLevel.Near, 1.75), "the control: a giant of 1.75 does not join on the near side");

        Assert.Equal((false, false), (reach.Joins(1, CutFloorLevel.Far, 1.75), reach.Joins(1, CutFloorLevel.Far, 1.9)));
    }

    /// <summary>Beyond the same corridor, a robot whose hull goes through it (scale 1.5, 72 wide in 80) joins, and so does an ordinary one.</summary>
    [Fact]
    public void A_robot_whose_hull_goes_through_the_corridor_joins_beyond_it()
    {
        var reach = new NavHullReach(CutFloorLevel.Nav);
        Assert.Equal((true, true), (reach.Joins(1, CutFloorLevel.Far, 1.5), reach.Joins(1, CutFloorLevel.Far, 1.0)));
    }

    /// <summary>The level's rooms rolled over many seeds at depth 5: with the navigation's reach, no robot above 1.5 stands in rooms 6, 16 or 17; without it, some do.</summary>
    [Fact]
    public void The_director_over_depth_1s_rooms_posts_no_giant_where_its_floor_is_cut_off()
    {
        var reach = new NavHullReach(Nav);
        var director = new SpawnDirector(TheRules);
        int without = 0, smallThere = 0;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            // Depth 1 rolls no giant: the same rooms and doors at depth 5, where giants roll.
            var level = HordeLevel.Create(seed, 5, FixtureLevel.Rooms, FixtureLevel.Doors);
            foreach (var r in director.Plan(level, 4).Packs.SelectMany(p => p.Robots))
                if (r.Room is 6 or 16 or 17 && r.Roll.Scale > 1.5)
                    without++;
            foreach (var r in director.Plan(level, 4, reach).Packs.SelectMany(p => p.Robots).Where(r => r.Room is 6 or 16 or 17))
            {
                Assert.True(r.Roll.Scale <= 1.5, $"seed {seed}: {r.Roll.Template} at {r.Roll.Scale} posted in room {r.Room}");
                smallThere++;
            }
        }

        Assert.True(without > 0, "the control: no robot above 1.5 was posted in rooms 6, 16 or 17 with no reach");
        Assert.True(smallThere > 100, $"{smallThere} robots in rooms 6, 16 and 17");
    }
}

/// <summary>
/// A level of its own whose floor a giant cannot cross (2026-11-15), for the facts about refusing a
/// hull the floor cannot carry: two rooms on one floor (top at 16), the wall between them (x 1008
/// to 1104) pierced by a corridor 80 wide (y 96 to 176), which a hull of scale 1.5 (72 wide) walks
/// through and one of 1.75 (84) or 1.9 (92) does not. Every face lies on a voxel's boundary, so the
/// cut does not rest on how a <c>.nav3d</c> holds a solid's top inside a voxel, as depth 1 seed 1's
/// floor rails did. The near room is the larger, so its floor is every class's main one. Written as
/// a <c>.nav3d</c> by map-tools' own writer: a voxel is solid when it overlaps a solid box, a
/// column's free runs are its leaves, and a leaf's clearance record holds a corner per solid not
/// wholly below it (its Chebyshev gap from the column, its bottom), dominated corners dropped.
/// </summary>
internal static class CutFloorLevel
{
    private const float Voxel = 16f, Top = 256f;
    private const int CellVoxels = 16, CellsX = 6, HeightVoxels = 16;

    public const float FloorZ = 16f;

    private static readonly (float X0, float Y0, float Z0, float X1, float Y1, float Z1)[] Solids =
    [
        (0f, 0f, 0f, CellsX * 256f, 256f, FloorZ),
        (1008f, 0f, FloorZ, 1104f, 96f, Top),
        (1008f, 176f, FloorZ, 1104f, 256f, Top),
    ];

    private static readonly Lazy<HordeNavData> Data = new(() => HordeNavData.Derive(Write()));

    public static HordeNavData Nav => Data.Value;

    /// <summary>The near room (0, x 0 to 1056) and the far one (1, x 1056 to 1536), the door between them in the corridor.</summary>
    public static HordeLevel Level => HordeLevel.Create(1, 5,
        [new HordeRoom(0, new AiVector(0f, 0f, 0f), new AiVector(1056f, 256f, Top), []), new HordeRoom(1, new AiVector(1056f, 0f, 0f), new AiVector(1536f, 256f, Top), [])],
        [new HordeDoor(0, 1, new AiVector(1056f, 136f, FloorZ), new AiVector(1f, 0f, 0f), 80f)]);

    /// <summary>A point on the floor at the near room's far end from the corridor.</summary>
    public static AiVector Near => new(24f, 136f, FloorZ);

    /// <summary>A point on the floor at the far room's far end.</summary>
    public static AiVector Far => new(1496f, 136f, FloorZ);

    /// <summary>Whether the cell under <paramref name="at"/> admits <paramref name="cls"/>.</summary>
    public static bool Stands(AiVector at, ClearanceClass cls)
    {
        var c = Nav.Graph.FindCell(at + new AiVector(0f, 0f, 18f));
        return c.IsValid && Nav.Graph.Cell(c).Admits(cls);
    }

    private static bool SolidAt(int gx, int gy, int z)
    {
        float x0 = gx * Voxel, y0 = gy * Voxel, z0 = z * Voxel;
        return z < 0 || Solids.Any(s => s.X0 < x0 + Voxel && x0 < s.X1 && s.Y0 < y0 + Voxel && y0 < s.Y1 && s.Z0 < z0 + Voxel && z0 < s.Z1);
    }

    private static byte[] Write()
    {
        var leaves = new List<Nav3dLeaf>();
        var starts = new List<uint> { 0 };
        var clearance = new List<byte>();
        var records = new Dictionary<string, uint>();
        for (int cell = 0; cell < CellsX; cell++)
        {
            for (int y = 0; y < CellVoxels; y++)
            {
                for (int x = 0; x < CellVoxels; x++)
                {
                    int gx = cell * CellVoxels + x;
                    for (int z = 0; z < HeightVoxels;)
                    {
                        if (SolidAt(gx, y, z))
                        {
                            z++;
                            continue;
                        }

                        int lo = z;
                        while (z < HeightVoxels && !SolidAt(gx, y, z))
                            z++;
                        byte[] record = Record(gx, y, lo);
                        string key = Convert.ToHexString(record);
                        if (!records.TryGetValue(key, out uint offset))
                        {
                            offset = (uint)clearance.Count;
                            records[key] = offset;
                            clearance.AddRange(record);
                        }

                        leaves.Add(Leaf(gx, y, lo, z - 1, offset));
                    }

                    starts.Add((uint)leaves.Count);
                }
            }
        }

        return Nav3dWriter.Write(new Nav3dLevel
        {
            CellSize = CellVoxels * Voxel,
            VoxelSize = Voxel,
            CellVoxels = CellVoxels,
            CellHeights = null,
            Columns = CellsX,
            Rows = 1,
            Origin = new Vec3(0, 0, 0),
            FloorNormalZ = 0.7f,
            StepHeight = 18,
            JumpHeight = 56,
            JumpDistance = 100,
            Cells = [.. Enumerable.Range(0, CellsX).Select(_ => new Nav3dCell(null, 0, Nav3dRoomRole.None, 0, 0))],
            Presets = [new Nav3dPreset("standing", 32, 72, Nav3dClipClass.Player)],
            Roots = [.. Enumerable.Range(0, CellsX).Select(c => c * CellVoxels * CellVoxels)],
            ColumnStarts = [.. starts],
            Leaves = [.. leaves],
            Clearance = [.. clearance],
            Obstacles = [],
            Jumps = [],
        });
    }

    private static Nav3dLeaf Leaf(int gx, int gy, int lo, int hi, uint record)
    {
        float x0 = gx * Voxel, y0 = gy * Voxel, z0 = lo * Voxel;
        bool grounded = lo > 0 && SolidAt(gx, gy, lo - 1);
        float floor = grounded
            ? Solids.Where(s => s.X0 < x0 + Voxel && x0 < s.X1 && s.Y0 < y0 + Voxel && y0 < s.Y1 && s.Z0 < z0 && z0 - Voxel < s.Z1).Max(s => s.Z1)
            : 0f;
        var flags = grounded
            ? Nav3dLeafFlags.GroundedPlayer | Nav3dLeafFlags.GroundedNpc | Nav3dLeafFlags.WalkablePlayer | Nav3dLeafFlags.WalkableNpc
            : Nav3dLeafFlags.None;
        return new Nav3dLeaf((byte)lo, (byte)(hi - lo + 1), flags, 256, record, record, floor, floor);
    }

    private static byte[] Record(int gx, int gy, int lo)
    {
        float x0 = gx * Voxel, y0 = gy * Voxel, x1 = x0 + Voxel, y1 = y0 + Voxel, bottom = lo * Voxel;
        var corners = new List<Nav3dCorner>();
        foreach (var s in Solids)
        {
            if (s.Z1 <= bottom)
                continue;
            float gap = MathF.Max(MathF.Max(s.X0 - x1, x0 - s.X1), MathF.Max(s.Y0 - y1, y0 - s.Y1));
            if (gap <= 256f)
                corners.Add(new Nav3dCorner(gap < 0f ? float.NegativeInfinity : gap, s.Z0));
        }

        var kept = new List<Nav3dCorner>();
        float lowest = float.PositiveInfinity;
        foreach (Nav3dCorner c in corners.OrderBy(c => c.Width).ThenBy(c => c.Top))
        {
            if (c.Top < lowest && (kept.Count == 0 || c.Width > kept[^1].Width))
            {
                kept.Add(c);
                lowest = c.Top;
            }
        }

        return Nav3dClearance.Encode([.. kept], [], []);
    }
}
