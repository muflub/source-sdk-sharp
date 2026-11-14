using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Jump;
using Xunit;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The simulator's free space as it is read from a nav (plan_bots_teams_tf2_part_g G.5 step 3):
/// a column a voxel column, a span a run of leaves that touch, its floor the leaf's own.
/// </summary>
public class VoxelColumnsTests
{
    /// <summary>A nav of nothing but leaves: every other question is one the columns must not ask.</summary>
    internal sealed class Leaves(params NavLeaf[] leaves) : INavSource
    {
        public float StepHeight => throw new NotSupportedException();

        public float JumpHeight => throw new NotSupportedException();

        public float VoxelSize => 16f;

        public int LeafCount => leaves.Length;

        public NavLeaf Leaf(int leaf) => leaves[leaf];

        public bool Stands(int leaf, float width, float height) => throw new NotSupportedException();

        public bool Climbs(int leaf, float width, float height) => throw new NotSupportedException();

        public bool FitsAt(int leaf, float originZ, float width, float height) => throw new NotSupportedException();

        public bool FitsAbove(int leaf, float feet, float width, float height) => throw new NotSupportedException();

        public int ColumnLeaves(int leaf, int dx, int dy, Span<int> into) => throw new NotSupportedException();

        public int Below(int leaf) => throw new NotSupportedException();

        public int Above(int leaf) => throw new NotSupportedException();

        public int Jumps(int leaf, Span<NavJumpLink> into) => throw new NotSupportedException();

        public int ObstacleCount => throw new NotSupportedException();

        public NavObstacleInfo Obstacle(int obstacle) => throw new NotSupportedException();

        public int ObstacleLeaves(int obstacle, Span<int> into) => throw new NotSupportedException();

        public bool StandsWithShut(int leaf, int obstacle, float width, float height) => throw new NotSupportedException();
    }

    /// <summary>A leaf of column (x, y) from the grid at (32, 64): its voxels from <paramref name="bottom"/> to <paramref name="top"/>.</summary>
    internal static NavLeaf Leaf(int x, int y, float bottom, float top, float? floor = null)
        => new(32f + x * 16f, 64f + y * 16f, 48f + x * 16f, 80f + y * 16f, bottom, top, floor ?? bottom, floor is not null, false, false, 1f);

    [Fact]
    public void TheGridSpansTheLeavesFromTheirLowCorner()
    {
        VoxelColumns c = VoxelColumns.FromNav(new Leaves(Leaf(0, 0, 0f, 64f), Leaf(3, 1, 0f, 64f)));
        Assert.Equal((32f, 64f, 16f, 4, 2), (c.OriginX, c.OriginY, c.VoxelSize, c.Width, c.Depth));
        Assert.Equal((0, 0), (c.ColumnX(32f), c.ColumnY(79.9f)));
        Assert.Equal((3, 1), (c.ColumnX(95f), c.ColumnY(80f)));
    }

    [Fact]
    public void ALeafsOwnFloorIsItsSpansBottom()
    {
        // The surface 6 units up a voxel: the leaf's voxels start at 160, the floor it stands on is 150.
        VoxelColumns c = VoxelColumns.FromNav(new Leaves(Leaf(0, 0, 160f, 320f, floor: 150f)));
        Assert.Equal([new FreeSpan(150f, 320f)], c.Spans(0, 0).ToArray());
    }

    [Fact]
    public void LeavesThatTouchAreOneSpanAndLeavesApartAreTwo()
    {
        // Listed out of order: the file's order is not the columns' to rely on.
        VoxelColumns c = VoxelColumns.FromNav(new Leaves(Leaf(0, 0, 400f, 480f, floor: 396f), Leaf(0, 0, 64f, 128f), Leaf(0, 0, 0f, 64f, floor: 0f)));
        Assert.Equal([new FreeSpan(0f, 128f), new FreeSpan(396f, 480f)], c.Spans(0, 0).ToArray());
    }

    [Fact]
    public void AColumnWithNoLeafHasNoSpanAndNorHasOneOutsideTheGrid()
    {
        VoxelColumns c = VoxelColumns.FromNav(new Leaves(Leaf(0, 0, 0f, 64f), Leaf(2, 0, 0f, 64f)));
        Assert.Equal(1, c.Spans(0, 0).Length);
        Assert.Equal(0, c.Spans(1, 0).Length);
        Assert.Equal(0, c.Spans(3, 0).Length);
        Assert.Equal(0, c.Spans(-1, 0).Length);
    }

    [Fact]
    public void ASpanThatIsEmptyOrOutOfOrderIsRefused()
    {
        Assert.Throws<ArgumentException>(() => VoxelColumns.Of(0f, 0f, 16f, 1, 1, (_, _) => [new(10f, 10f)]));
        Assert.Throws<ArgumentException>(() => VoxelColumns.Of(0f, 0f, 16f, 1, 1, (_, _) => [new(100f, 200f), new(0f, 50f)]));
    }

    [Fact]
    public void ANavWithNoLeafIsRefused()
        => Assert.Contains("no leaf", Assert.Throws<ArgumentException>(() => VoxelColumns.FromNav(new Leaves())).Message);
}
