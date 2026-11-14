using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using Xunit;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The hull sweep through the nav's free space (plan_bots_teams_tf2_part_g G.5 step 3), on
/// worlds small enough to work out by hand: 16 columns a side of 16 units, a floor at 0 and a
/// ceiling at 512 unless a fact says otherwise. The hull is TF2's: 48 across, 82 tall.
/// </summary>
public class VoxelMoveWorldTests
{
    private const float Eps = VoxelMoveWorld.DistEpsilon;

    private static readonly Vector Mins = new(-24f, -24f, 0f), Maxs = new(24f, 24f, 82f);

    private static readonly FreeSpan[] Room = [new(0f, 512f)];

    /// <summary>A world of 16 by 16 columns from the origin; <paramref name="spans"/> answers a column, null for the open room.</summary>
    internal static VoxelMoveWorld World(Func<int, int, FreeSpan[]?>? spans = null)
        => new(VoxelColumns.Of(0f, 0f, 16f, 16, 16, (x, y) => spans?.Invoke(x, y) ?? Room));

    private static MoveTrace Sweep(VoxelMoveWorld world, Vector start, Vector end) => world.Trace(start, end, Mins, Maxs, 0u, 0);

    /// <summary>A wall from x = 128 east: its columns have no free span.</summary>
    private static VoxelMoveWorld Walled() => World((x, _) => x >= 8 ? [] : null);

    /// <summary>A ledge 64 high from x = 128 east.</summary>
    private static VoxelMoveWorld Ledged() => World((x, _) => x >= 8 ? [new(64f, 512f)] : null);

    /// <summary>A pit 64 wide and 512 deep across x = 96 to 160.</summary>
    private static VoxelMoveWorld Gapped() => World((x, _) => x is >= 6 and < 10 ? [new(-512f, 512f)] : null);

    [Fact]
    public void AHullDroppedOntoAFloorStopsAnEpsilonOverIt()
    {
        MoveTrace t = Sweep(World(), new Vector(100f, 100f, 10f), new Vector(100f, 100f, -10f));
        Assert.Equal((10f - Eps) / 20f, t.Fraction, 5);
        Assert.Equal(Eps, t.EndPos.Z, 4);
        Assert.Equal(new Vector(0f, 0f, 1f), t.PlaneNormal);
        Assert.False(t.StartSolid);
    }

    [Fact]
    public void AHullRestingOnAFloorSlidesAlongItUnhindered()
    {
        // Across eight columns' floors: no column's side is in the way of one an epsilon over them all.
        MoveTrace t = Sweep(World(), new Vector(40f, 100f, Eps), new Vector(200f, 100f, Eps));
        Assert.Equal(1f, t.Fraction);
        Assert.False(t.StartSolid);
    }

    [Fact]
    public void AHullWalkedIntoAWallStopsAnEpsilonShortOfItWithTheWallsNormal()
    {
        MoveTrace t = Sweep(Walled(), new Vector(50f, 100f, Eps), new Vector(150f, 100f, Eps));
        Assert.Equal(128f - 24f - Eps, t.EndPos.X, 3);
        Assert.Equal(new Vector(-1f, 0f, 0f), t.PlaneNormal);
        Assert.Equal(100f, t.EndPos.Y);
    }

    [Fact]
    public void AHullRisingIntoACeilingStopsUnderItWithTheNormalDown()
    {
        MoveTrace t = Sweep(World((_, _) => [new(0f, 128f)]), new Vector(100f, 100f, 10f), new Vector(100f, 100f, 100f));
        Assert.Equal(128f - 82f - Eps, t.EndPos.Z, 3);
        Assert.Equal(new Vector(0f, 0f, -1f), t.PlaneNormal);
    }

    [Fact]
    public void AHullUnderALedgesTopIsStoppedByItsFace()
    {
        MoveTrace t = Sweep(Ledged(), new Vector(50f, 100f, 40f), new Vector(150f, 100f, 40f));
        Assert.Equal(128f - 24f - Eps, t.EndPos.X, 3);
        Assert.Equal(new Vector(-1f, 0f, 0f), t.PlaneNormal);
    }

    [Fact]
    public void AHullOverALedgesTopCrossesOntoItAndLandsOnIt()
    {
        VoxelMoveWorld world = Ledged();
        Assert.Equal(1f, Sweep(world, new Vector(50f, 100f, 64f + Eps), new Vector(150f, 100f, 64f + Eps)).Fraction);
        MoveTrace down = Sweep(world, new Vector(150f, 100f, 80f), new Vector(150f, 100f, 0f));
        Assert.Equal(64f + Eps, down.EndPos.Z, 3);
        Assert.Equal(new Vector(0f, 0f, 1f), down.PlaneNormal);
    }

    [Fact]
    public void AHullWhollyOverAGapDropsIntoIt()
    {
        // The pit is 64 wide and the hull 48: centred, it touches neither lip.
        MoveTrace t = Sweep(Gapped(), new Vector(128f, 100f, 10f), new Vector(128f, 100f, -200f));
        Assert.Equal(1f, t.Fraction);
    }

    [Fact]
    public void AHullWithAnEdgeOverAGapsLipStandsOnTheLip()
    {
        // 16 units west of centre the hull's west edge is 8 units over the lip's floor.
        MoveTrace t = Sweep(Gapped(), new Vector(112f, 100f, 10f), new Vector(112f, 100f, -200f));
        Assert.Equal(Eps, t.EndPos.Z, 3);
        Assert.Equal(new Vector(0f, 0f, 1f), t.PlaneNormal);
    }

    [Fact]
    public void AHullInAGapIsStoppedByItsFarSide()
    {
        MoveTrace t = Sweep(Gapped(), new Vector(128f, 100f, -100f), new Vector(228f, 100f, -100f));
        Assert.Equal(160f - 24f - Eps, t.EndPos.X, 3);
        Assert.Equal(new Vector(-1f, 0f, 0f), t.PlaneNormal);
    }

    [Fact]
    public void AHullThatStartsInSolidSaysSoAndDoesNotMove()
    {
        Vector start = new(100f, 100f, -5f);
        MoveTrace t = Sweep(World(), start, new Vector(100f, 100f, -20f));
        Assert.True(t.StartSolid);
        Assert.True(t.AllSolid);
        Assert.Equal(0f, t.Fraction);
        Assert.Equal(start, t.EndPos);
    }

    [Fact]
    public void AHullThatStartsInSolidAndLeavesItIsNotSolidAllTheWay()
    {
        MoveTrace t = Sweep(World(), new Vector(100f, 100f, -5f), new Vector(100f, 100f, 50f));
        Assert.True(t.StartSolid);
        Assert.False(t.AllSolid);
    }

    [Fact]
    public void OutsideTheGridIsSolid()
    {
        // The grid ends at x = 256 with no wall of its own.
        MoveTrace t = Sweep(World(), new Vector(200f, 100f, Eps), new Vector(300f, 100f, Eps));
        Assert.Equal(256f - 24f - Eps, t.EndPos.X, 3);
        Assert.Equal(new Vector(-1f, 0f, 0f), t.PlaneNormal);
    }

    [Fact]
    public void ARayIsAHullOfNoSizeAndStopsAtTheFirstSolid()
    {
        MoveTrace t = World().Trace(new Vector(100f, 100f, 100f), new Vector(100f, 100f, -100f), Vector.Zero, Vector.Zero, 0u, 0);
        Assert.Equal(Eps, t.EndPos.Z, 3);
        Assert.Equal(new Vector(0f, 0f, 1f), t.PlaneNormal);
    }

    [Fact]
    public void ASlantedSweepStopsAtTheNearerOfAWallAndAFloor()
    {
        // East 40 and down 100 from 50 up, 4 units off the wall: the wall comes first, a tenth of the way.
        MoveTrace t = Sweep(Walled(), new Vector(100f, 100f, 50f), new Vector(140f, 100f, -50f));
        Assert.Equal(new Vector(-1f, 0f, 0f), t.PlaneNormal);
        Assert.Equal((4f - Eps) / 40f, t.Fraction, 4);
    }

    [Fact]
    public void ASecondSpanOverTheFirstIsARoomOfItsOwn()
    {
        VoxelMoveWorld world = World((_, _) => [new(0f, 100f), new(200f, 320f)]);
        MoveTrace down = Sweep(world, new Vector(100f, 100f, 210f), new Vector(100f, 100f, 150f));
        Assert.Equal(200f + Eps, down.EndPos.Z, 3);
        Assert.Equal(new Vector(0f, 0f, 1f), down.PlaneNormal);
        MoveTrace up = Sweep(world, new Vector(100f, 100f, 210f), new Vector(100f, 100f, 300f));
        Assert.Equal(320f - 82f - Eps, up.EndPos.Z, 3);
        Assert.Equal(new Vector(0f, 0f, -1f), up.PlaneNormal);
    }

    [Fact]
    public void AClearSweepEndsWhereItWasSentWithNoPlane()
    {
        Vector end = new(150f, 120f, 200f);
        MoveTrace t = Sweep(World(), new Vector(100f, 100f, 100f), end);
        Assert.Equal(1f, t.Fraction);
        Assert.Equal(end, t.EndPos);
        Assert.Equal(Vector.Zero, t.PlaneNormal);
    }
}
