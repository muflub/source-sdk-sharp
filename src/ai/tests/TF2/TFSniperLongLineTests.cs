using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The long-line sight pass (plan_bots_teams_tf2 C.3.2, round2.md §5 "Sniper"): past the near table's 1,024 u, out to
/// <see cref="NavSight.LongRange"/>, on a coarser grid, with the near table's answers left as they were; and the Sniper's
/// spots standing on its lines past the near table's reach.
/// </summary>
public sealed class TFSniperLongLineTests
{
    /// <summary>Free space everywhere but a wall across the floor at x 1,900..1,916 (when there is one).</summary>
    private sealed class Space(bool wall) : IVoxelSpace
    {
        public bool Free(AiVector p) => !wall || p.X < 1900f || p.X > 1916f;
    }

    private static NavGraph Floor => TFField.Floor();

    private static int Cell(NavGraph g, float x, float y) => g.FindCell(new AiVector(x, y, 1f)).Index;

    /// <summary>Two places 2,900 u apart in open space: the long pass sees one from the other; the near table, out to 1,024 u, does not.</summary>
    [Fact]
    public void AFarPairIsSeenByTheLongPassAndNotTheNearTable()
    {
        NavGraph g = Floor;
        NavSight sight = NavSight.Build(g, new Space(wall: false));
        int a = Cell(g, 32f, 256f), b = Cell(g, 2950f, 256f);
        Assert.Equal((false, true), (sight.Visible(a, b), sight.VisibleFar(a, b)));
    }

    /// <summary>A wall between them hides the far pair from the long pass too.</summary>
    [Fact]
    public void AWallBetweenAFarPairHidesIt()
    {
        NavGraph g = Floor;
        NavSight sight = NavSight.Build(g, new Space(wall: true));
        int a = Cell(g, 32f, 256f), b = Cell(g, 2950f, 256f);
        Assert.False(sight.VisibleFar(a, b));
    }

    /// <summary>Past the long range (3,000 u) nothing is seen; just inside it, it is.</summary>
    [Fact]
    public void PastTheLongRangeNothingIsSeen()
    {
        NavGraph g = Floor;
        NavSight sight = NavSight.Build(g, new Space(wall: false));
        int a = Cell(g, 32f, 256f);
        Assert.Equal((true, false), (sight.VisibleFar(a, Cell(g, 2900f, 256f)), sight.VisibleFar(a, Cell(g, 4060f, 256f))));
    }

    /// <summary>The near table's answers, its range and its exposure are the same with the long pass built and without it (HL2MP's replays read only them).</summary>
    [Fact]
    public void TheNearTableIsTheSameWithTheLongPass()
    {
        NavGraph g = Floor;
        var space = new Space(wall: true);
        NavSight with = NavSight.Build(g, space), without = NavSight.Build(g, space, longLines: false);
        Assert.True(with.LongPairs > 0 && without.LongPairs == 0, $"long pairs {with.LongPairs} / {without.LongPairs}");
        Assert.Equal(without.Pairs, with.Pairs);
        int differ = 0, seen = 0;
        for (int a = 0; a < g.CellCount; a += 3)
        {
            differ += with.Exposure(a) == without.Exposure(a) ? 0 : 1;
            for (int b = 0; b < g.CellCount; b += 5)
            {
                bool v = with.Visible(a, b);
                seen += v ? 1 : 0;
                differ += v == without.Visible(a, b) ? 0 : 1;
            }
        }

        Assert.True(seen > 0, "nothing seen at all");
        Assert.Equal(0, differ);
    }

    /// <summary>A sight table that sees everything to its range; with <c>far</c>, a long pass that sees everything to 3,000 u.</summary>
    private class Sees : ICellSight
    {
        private readonly NavGraph _g;

        public Sees(NavGraph g) => _g = g;

        public float Range => 1024f;

        protected float Apart(int a, int b) => _g.Cell(a).Centre.DistanceTo(_g.Cell(b).Centre);

        public bool Visible(int a, int b) => Apart(a, b) <= Range;

        public float Exposure(int cell) => 0f;
    }

    private sealed class SeesFar(NavGraph g) : Sees(g), ILongSight
    {
        public float LongRange => 3000f;

        public bool VisibleFar(int a, int b) => Apart(a, b) <= LongRange;
    }

    private static float Farthest(ICellSight sight)
    {
        NavGraph g = Floor;
        AiVector watch = new(2048f, 256f, 0f);
        var spots = TFSniperSpots.Find(g, sight, null, watch, TFField.RedSpawn, attack: true);
        Assert.NotEmpty(spots);
        return spots.Max(s => s.At.DistanceTo(watch));
    }

    /// <summary>With only the near table a Sniper's spots stand within its band (1,100 u); with the long pass, one stands past it.</summary>
    [Fact]
    public void WithTheLongPassASpotStandsPastTheNearTable()
    {
        NavGraph g = Floor;
        float near = Farthest(new Sees(g)), far = Farthest(new SeesFar(g));
        Assert.True(near <= TFSniperSpots.FarthestUnits && far > TFSniperSpots.FarthestUnits, $"near {near} far {far}");
    }
}
