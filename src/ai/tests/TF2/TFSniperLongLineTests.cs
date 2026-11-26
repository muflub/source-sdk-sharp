using SourceSharp.Tests;
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
        NavSight sight = NavSight.Build(g, new Space(wall: false), longLines: true);
        int a = Cell(g, 32f, 256f), b = Cell(g, 2950f, 256f);
        Assert.Equal((false, true), (sight.Visible(a, b), sight.VisibleFar(a, b)));
    }

    /// <summary>A wall between them hides the far pair from the long pass too.</summary>
    [Fact]
    public void AWallBetweenAFarPairHidesIt()
    {
        NavGraph g = Floor;
        NavSight sight = NavSight.Build(g, new Space(wall: true), longLines: true);
        int a = Cell(g, 32f, 256f), b = Cell(g, 2950f, 256f);
        Assert.False(sight.VisibleFar(a, b));
    }

    /// <summary>Past the long range (3,000 u) nothing is seen; just inside it, it is.</summary>
    [Fact]
    public void PastTheLongRangeNothingIsSeen()
    {
        NavGraph g = Floor;
        NavSight sight = NavSight.Build(g, new Space(wall: false), longLines: true);
        int a = Cell(g, 32f, 256f);
        Assert.Equal((true, false), (sight.VisibleFar(a, Cell(g, 2900f, 256f)), sight.VisibleFar(a, Cell(g, 4060f, 256f))));
    }

    /// <summary>The near table's answers, its range and its exposure are the same with the long pass built and without it (HL2MP's replays read only them).</summary>
    [Fact]
    public void TheNearTableIsTheSameWithTheLongPass()
    {
        NavGraph g = Floor;
        var space = new Space(wall: true);
        NavSight with = NavSight.Build(g, space, longLines: true), without = NavSight.Build(g, space);
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

    /// <summary>The long pass cast on the thread pool answers as the serial loop does, pair for pair (the review's L2).</summary>
    [Fact]
    public void TheThreadedLongPassIsTheSerialOne()
    {
        NavGraph g = Floor;
        var space = new Space(wall: true);
        NavSight threaded = NavSight.Build(g, space, longLines: true), serial = NavSight.Build(g, space, longLines: true, serialLongPass: true);
        Assert.Equal(serial.LongPairs, threaded.LongPairs);
        int differ = 0, seen = 0, hidden = 0;
        for (int a = 0; a < g.CellCount; a += 2)
        {
            for (int b = 0; b < g.CellCount; b += 3)
            {
                bool v = threaded.VisibleFar(a, b);
                (seen, hidden) = v ? (seen + 1, hidden) : (seen, hidden + 1);
                differ += v == serial.VisibleFar(a, b) ? 0 : 1;
            }
        }

        Assert.True(seen > 0 && hidden > 0, $"seen {seen} hidden {hidden}");
        Assert.Equal(0, differ);
    }

    /// <summary>
    /// Two lopsided 256 u squares (the review's L1): in each, one lone cell at the corner facing away from the other square and a
    /// full 128 u node at the corner facing it, so each square's eye stands about 181 u from the lone cell's own node's eye, toward
    /// the other. The lone cells' near eyes are 1,059 u apart (past the near table); their squares' eyes 730 u (under the old
    /// band's 768 u start).
    /// </summary>
    private static (NavGraph Graph, int A, int B) LopsidedPair()
    {
        var b = new NavGraphBuilder();
        CellId Cell(float cx, float cy) => b.AddCell(new NavCell(cx - 32f, cy - 32f, cx + 32f, cy + 32f, 0f,
                                                                 ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
        CellId a = Cell(1056f, 32f);
        foreach ((float x, float y) in new[] { (1184f, 160f), (1248f, 160f), (1184f, 224f), (1248f, 224f) })
            Cell(x, y);
        CellId c = Cell(2016f, 480f);
        foreach ((float x, float y) in new[] { (1824f, 288f), (1888f, 288f), (1824f, 352f), (1888f, 352f) })
            Cell(x, y);
        return (b.Build(), a.Index, c.Index);
    }

    /// <summary>No gap between the tables: the lopsided pair, on a clear line just past the near table's reach, is seen by the long pass.</summary>
    [Fact]
    public void JustPastTheNearRangeAClearLineIsSeen()
    {
        (NavGraph g, int a, int b) = LopsidedPair();
        NavSight sight = NavSight.Build(g, new Space(wall: false), longLines: true);
        float near = sight.Eye(sight.NodeOf(a)).DistanceTo(sight.Eye(sight.NodeOf(b)));
        Assert.True(near > NavSight.Range, $"near eyes {near:F0} u apart");
        Assert.True(sight.VisibleFar(a, b));
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
    private sealed class Plain : INavProfile
    {
        public NavNumbers Numbers(float fileJumpHeight) => NavNumbers.Stock(fileJumpHeight);

        public JumpReach Reach(SourceSharp.Abi.EntityHandle player) => JumpReach.None;

        public IReadOnlyList<TeamSolid> TeamSolids(MapEntities entities) => [];
    }

    /// <summary>Only TF2's profile asks its levels for the long pass (its Snipers read it); a game that does not ask (HL2MP) builds none.</summary>
    [Fact]
    public void OnlyTF2AsksForTheLongPass()
        => Assert.Equal((true, false), (((INavProfile)new SourceSharp.Ai.TF2.Nav.TFNavProfile(null!, null!, null!, null!)).LongSightLines, ((INavProfile)new Plain()).LongSightLines));
    /// <summary>
    /// The game's three sight builds (AiNav's load, the nav build job, the room job) each ask the level's profile for the long
    /// pass: every method of the server binding that calls NavSight.Build also reads INavProfile.LongSightLines (the re-review's
    /// N1: a false at any one call site left TF2's Snipers with no long line and every fact green).
    /// </summary>
    [Fact]
    public void TheGamesSightBuildsAskTheProfileForTheLongPass()
    {
        const System.Reflection.BindingFlags every = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                                                     | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                                                     | System.Reflection.BindingFlags.DeclaredOnly;
        var builds = new List<string>();
        var asks = new List<string>();
        foreach (Type type in typeof(AiNav).Assembly.GetTypes())
        {
            foreach (System.Reflection.MethodBase m in type.GetMethods(every).Cast<System.Reflection.MethodBase>().Concat(type.GetConstructors(every)))
            {
                System.Reflection.MethodBase[] callees = [.. IlCallCensus.CalleesOf(m)];
                if (!callees.Any(c => c.DeclaringType == typeof(NavSight) && c.Name == nameof(NavSight.Build)))
                    continue;
                builds.Add($"{type.Name}.{m.Name}");
                if (callees.Any(c => c.DeclaringType == typeof(INavProfile) && c.Name == "get_" + nameof(INavProfile.LongSightLines)))
                    asks.Add($"{type.Name}.{m.Name}");
            }
        }

        Assert.True(builds.Count >= 3, $"sight builds found: {string.Join(", ", builds)}");
        Assert.Equal(builds, asks);
    }
}
