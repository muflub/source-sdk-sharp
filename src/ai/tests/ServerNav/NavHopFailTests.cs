using SourceSharp.Ai.Server.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// Round 2's S14: a plain jump up on a route that the body rises off and comes back down from on the take-off's floor
/// (ctf_2fort's bridge ends: a jump over a column with no floor under the sky's, which bots tried for 28 s at a time). Two
/// such tries at one face since a jump there landed, and the follower routes the other way; with no other way it keeps the
/// jump. A level or downward jump is not watched. The cells are the derived graphs' 16 u.
/// </summary>
public class NavHopFailTests
{
    private const ClearanceClasses Both = ClearanceClasses.Stand | ClearanceClasses.Crouch;
    private const float C = 16f;

    private static CellId Cell(NavGraphBuilder b, float x, float y, float z) => b.AddCell(new NavCell(x, y, x + C, y + C, z, Both, NavCellFlags.None, 1f));

    /// <summary>
    /// <paramref name="rows"/> rows of a deck three cells long (x 0..48, z 0) and of a ledge 50 u up two columns past it
    /// (x 64..80; no cell at x 48..64), a jump from each row's deck end to its ledge, the rows walked between; with
    /// <paramref name="stairs"/>, a long way round too: north up eight steps from the first row's deck start, east, and south
    /// along x 64..80 to the ledges. The take-offs and ledges are by row.
    /// </summary>
    private static (NavGraph G, CellId[] TakeOff, CellId[] Ledge) Deck(bool stairs, int rows = 1)
    {
        var b = new NavGraphBuilder();
        var deck = new CellId[rows, 3];
        var takeOff = new CellId[rows];
        var ledge = new CellId[rows];
        for (int r = 0; r < rows; r++)
        {
            for (int i = 0; i < 3; i++)
            {
                deck[r, i] = Cell(b, i * C, r * C, 0f);
                if (i > 0)
                    b.AddBoth(deck[r, i - 1], deck[r, i], NavEdgeType.Walk);
                if (r > 0)
                    b.AddBoth(deck[r - 1, i], deck[r, i], NavEdgeType.Walk);
            }

            takeOff[r] = deck[r, 2];
            ledge[r] = Cell(b, 64f, r * C, 50f);
            if (r > 0)
                b.AddBoth(ledge[r - 1], ledge[r], NavEdgeType.Walk);
            b.AddEdge(takeOff[r], ledge[r], NavEdgeType.Jump);
            b.AddEdge(ledge[r], takeOff[r], NavEdgeType.Drop);
        }

        if (stairs)
        {
            float y0 = rows * C;
            CellId last = deck[0, 0];
            // Up the stairs at x 0..16, then a long walk on z 50 east and back south to the last row's ledge.
            for (int i = 0; i < 8; i++)
            {
                CellId s = Cell(b, 0f, y0 + (i * C), 6.25f * (i + 1));
                b.AddBoth(last, s, NavEdgeType.Walk);
                last = s;
            }

            for (int i = 1; i <= 4; i++)
            {
                CellId s = Cell(b, i * C, y0 + (7 * C), 50f);
                b.AddBoth(last, s, NavEdgeType.Walk);
                last = s;
            }

            for (int i = 6; i >= 0; i--)
            {
                CellId s = Cell(b, 64f, y0 + (i * C), 50f);
                b.AddBoth(last, s, NavEdgeType.Walk);
                last = s;
            }

            b.AddBoth(last, ledge[rows - 1], NavEdgeType.Walk);
        }

        return (b.Build(), takeOff, ledge);
    }

    /// <summary>A crouch-jump at the face: up 71 u and back to the deck, a tick at a time, then a quarter second on it.</summary>
    private static void FallBack(NavFollower f, AiVector at)
    {
        float z = 0f, vz = 289f;
        for (int t = 0; t < 70; t++)
        {
            f.Next(at with { Z = at.Z + z }, out _);
            z = MathF.Max(0f, z + (vz * 0.015f));
            vz = z > 0f ? vz - 12f : 0f;
        }
    }

    /// <summary>A crouch-jump from <paramref name="at"/> that goes on to the ledge's floor at x 72: up and across, and on it.</summary>
    private static void LandOnTheLedge(NavFollower f, AiVector at)
    {
        float z = 0f, vz = 289f, x = at.X;
        for (int t = 0; t < 40; t++)
        {
            f.Next(new AiVector(x, at.Y, x >= 56f ? MathF.Max(z, 50f) : z), out _);
            z += vz * 0.015f;
            vz -= 12f;
            x = MathF.Min(x + 2f, 72f);
        }
    }

    private static NavFollower Toward(NavGraph g, CellId to) => new(g, to, g.Cell(to).Centre, ClearanceClass.Crouch, new NavObstacleState(0));

    /// <summary>The control: before any try, and after one, the jump is the way from the deck's end.</summary>
    [Fact]
    public void OneFailedJumpLeavesTheJumpTheWay()
    {
        var (g, takeOff, ledge) = Deck(stairs: true);
        NavFollower f = Toward(g, ledge[0]);
        AiVector at = g.Cell(takeOff[0]).Centre;
        f.Next(at, out _);
        Assert.Equal(NavEdgeType.Jump, f.NextVia);
        FallBack(f, at);
        Assert.Equal((1, NavEdgeType.Jump), (f.HopsFailed, f.NextVia));
    }

    /// <summary>Two tries that came back down on the deck: the follower walks the long way round, and so does the one it goes on to.</summary>
    [Fact]
    public void TwoFailedJumpsSendTheBotTheOtherWay()
    {
        var (g, takeOff, ledge) = Deck(stairs: true);
        NavFollower f = Toward(g, ledge[0]);
        AiVector at = g.Cell(takeOff[0]).Centre;
        f.Next(at, out _);
        FallBack(f, at);
        FallBack(f, at);
        Assert.Equal(2, f.HopsFailed);
        Assert.Equal(NavEdgeType.Walk, f.NextVia);
        NavFollower next = Toward(g, ledge[0]);
        next.GoOnFrom(f);
        next.Next(at, out _);
        Assert.Equal(NavEdgeType.Walk, next.NextVia);
    }

    /// <summary>A regoal between the tries (a hunt's, every half second) does not start the count again: the tries are the face's.</summary>
    [Fact]
    public void TheTriesCountAcrossARegoal()
    {
        var (g, takeOff, ledge) = Deck(stairs: true);
        NavFollower f = Toward(g, ledge[0]);
        AiVector at = g.Cell(takeOff[0]).Centre;
        f.Next(at, out _);
        FallBack(f, at);
        NavFollower next = Toward(g, ledge[0]);
        next.GoOnFrom(f);
        next.Next(at, out _);
        Assert.Equal(NavEdgeType.Jump, next.NextVia);
        FallBack(next, at);
        Assert.Equal(NavEdgeType.Walk, next.NextVia);
    }

    /// <summary>A regoal in the air, at the jump's top: the follower it goes on to still sees the body come back down on the deck.</summary>
    [Fact]
    public void AJumpInFlightOverARegoalIsStillWatched()
    {
        var (g, takeOff, ledge) = Deck(stairs: true);
        NavFollower f = Toward(g, ledge[0]);
        AiVector at = g.Cell(takeOff[0]).Centre;
        f.Next(at, out _);
        float z = 0f, vz = 289f;
        NavFollower flying = f;
        for (int t = 0; t < 70; t++)
        {
            if (t == 24)
            {
                // At the top: a new goal's follower, as the driver makes one.
                NavFollower next = Toward(g, ledge[0]);
                next.GoOnFrom(flying);
                flying = next;
            }

            flying.Next(at with { Z = at.Z + z }, out _);
            z = MathF.Max(0f, z + (vz * 0.015f));
            vz = z > 0f ? vz - 12f : 0f;
        }

        Assert.Equal((0, 1), (f.HopsFailed, flying.HopsFailed));
    }

    /// <summary>
    /// A body coming down onto the take-off from 60 u up (dropped there, or a fall the follower did not see begin) took off
    /// on no jump: the follower that first sees it in the air counts no failure when it lands.
    /// </summary>
    [Fact]
    public void ABodyComingDownOnTheTakeOffTookOffOnNoJump()
    {
        var (g, takeOff, ledge) = Deck(stairs: true);
        NavFollower f = Toward(g, ledge[0]);
        AiVector at = g.Cell(takeOff[0]).Centre;
        float z = 60f, vz = 0f;
        for (int t = 0; t < 40; t++)
        {
            f.Next(at with { Z = at.Z + z }, out _);
            z = MathF.Max(0f, z + (vz * 0.015f));
            vz = z > 0f ? vz - 12f : 0f;
        }

        Assert.Equal(0, f.HopsFailed);
    }

    /// <summary>A jump that landed between two that fell back clears the face's count: the jump is still the way.</summary>
    [Fact]
    public void ALandedJumpClearsTheFacesCount()
    {
        var (g, takeOff, ledge) = Deck(stairs: true);
        NavFollower f = Toward(g, ledge[0]);
        AiVector at = g.Cell(takeOff[0]).Centre;
        f.Next(at, out _);
        FallBack(f, at);
        LandOnTheLedge(f, at);
        f.Next(at, out _);
        FallBack(f, at);
        Assert.Equal((2, NavEdgeType.Jump), (f.HopsFailed, f.NextVia));
    }

    /// <summary>
    /// Two take-offs side by side, each with its jump to the ledge beside the other's: one fall from each is two at that
    /// face, and the face's jumps are left out, as the blacklist reaches them, not one count per take-off cell.
    /// </summary>
    [Fact]
    public void FallsFromNeighbouringTakeOffsCountAtTheirFace()
    {
        var (g, takeOff, ledge) = Deck(stairs: true, rows: 2);
        NavFollower f = Toward(g, ledge[0]);
        AiVector first = g.Cell(takeOff[0]).Centre, second = g.Cell(takeOff[1]).Centre;
        f.Next(first, out _);
        FallBack(f, first);
        f.Next(second, out _);
        Assert.Equal(NavEdgeType.Jump, f.NextVia);
        FallBack(f, second);
        Assert.Equal((2, NavEdgeType.Walk), (f.HopsFailed, f.NextVia));
    }

    /// <summary>The followers' tally counts each failure (the dev API's <c>ai.follow.hops_failed</c>), and a lane's counters add it up.</summary>
    [Fact]
    public void TheTallyCountsTheFailuresAndTheCountersAddThemUp()
    {
        var (g, takeOff, ledge) = Deck(stairs: true);
        var tally = new NavBuildTally();
        NavFollower f = new(g, ledge[0], g.Cell(ledge[0]).Centre, ClearanceClass.Crouch, new NavObstacleState(0)) { Tally = tally };
        AiVector at = g.Cell(takeOff[0]).Centre;
        f.Next(at, out _);
        FallBack(f, at);
        FallBack(f, at);
        var one = new SourceSharp.Ai.Server.DriveCounters(0, 0, 0, 0, 0, 0, 0, 0, tally.HopsFailed);
        Assert.Equal((2L, 4L), (tally.HopsFailed, (one + one).HopsFailed));
        // Where: each failure's face, by its take-off's and landing's cells.
        Assert.Equal("(40 8 0)>(72 8 50)|(40 8 0)>(72 8 50)", tally.LastHops);
    }

    /// <summary>
    /// A rail's top hopped over on a slant (the re-review's 45 degree path, HL2MP's commonest watched edge): the take-off cell
    /// centred (0 0 0), a rail top 32 u north at z 40, the body leaving from (6 -6) at 45 degrees, over the rail beside its
    /// cell's box and down on the far side, past the landing, at the take-off's height. It went over: no failure. The
    /// control, the 2fort trap's shape, is <see cref="OneFailedJumpLeavesTheJumpTheWay"/>: down at the face, short of it.
    /// </summary>
    [Fact]
    public void ARailHoppedOverOnASlantIsNoFailure()
    {
        var b = new NavGraphBuilder();
        CellId takeOff = b.AddCell(new NavCell(-8f, -8f, 8f, 8f, 0f, Both, NavCellFlags.None, 1f));
        CellId rail = b.AddCell(new NavCell(-8f, 24f, 8f, 40f, 40f, Both, NavCellFlags.None, 1f));
        CellId far = b.AddCell(new NavCell(-8f, 56f, 8f, 72f, 0f, Both, NavCellFlags.None, 1f));
        b.AddEdge(takeOff, rail, NavEdgeType.Jump);
        b.AddEdge(rail, far, NavEdgeType.Drop);
        NavGraph g = b.Build();
        NavFollower f = Toward(g, far);
        var at = new AiVector(6f, -6f, 0f);
        f.Next(at, out _);
        Assert.Equal(NavEdgeType.Jump, f.NextVia);
        float z = 0f, vz = 289f;
        for (int t = 0; t < 70; t++)
        {
            f.Next(new AiVector(at.X + (2f * t), at.Y + (2f * t), z), out _);
            z = MathF.Max(0f, z + (vz * 0.015f));
            vz = z > 0f ? vz - 12f : 0f;
        }

        Assert.Equal(0, f.HopsFailed);
    }

    /// <summary>With no other way up, the jump stays the way however often it failed: a bot trying it beats one with no route.</summary>
    [Fact]
    public void AJumpThatIsTheOnlyWayIsKept()
    {
        var (g, takeOff, ledge) = Deck(stairs: false);
        NavFollower f = Toward(g, ledge[0]);
        AiVector at = g.Cell(takeOff[0]).Centre;
        f.Next(at, out _);
        FallBack(f, at);
        FallBack(f, at);
        Assert.Equal(2, f.HopsFailed);
        Assert.Equal(NavEdgeType.Jump, f.NextVia);
    }

    /// <summary>A face found to be the only way is remembered: tried and failed again, it costs no field build.</summary>
    [Fact]
    public void AnOnlyWayJumpFailingAgainBuildsNoField()
    {
        var (g, takeOff, ledge) = Deck(stairs: false);
        NavFollower f = Toward(g, ledge[0]);
        AiVector at = g.Cell(takeOff[0]).Centre;
        f.Next(at, out _);
        FallBack(f, at);
        FallBack(f, at);
        int builds = f.Builds;
        FallBack(f, at);
        Assert.Equal((3, builds), (f.HopsFailed, f.Builds));
    }

    /// <summary>
    /// A jump across a level gap (a link of more than one column, a Jump with no rise: take-off x 0..16, landing x 96..112, a
    /// floor on past it) that a running body (240 u/s) comes down well past: no failure, though it lands at the take-off's height.
    /// </summary>
    [Fact]
    public void ALevelGapJumpRunOnPastIsNoFailure()
    {
        var b = new NavGraphBuilder();
        CellId takeOff = Cell(b, 0f, 0f, 0f);
        CellId landing = Cell(b, 96f, 0f, 0f);
        CellId last = landing;
        for (int i = 1; i <= 8; i++)
        {
            CellId s = Cell(b, 96f + (i * C), 0f, 0f);
            b.AddBoth(last, s, NavEdgeType.Walk);
            last = s;
        }

        b.AddEdge(takeOff, landing, NavEdgeType.Jump);
        NavGraph g = b.Build();
        NavFollower f = Toward(g, last);
        AiVector at = g.Cell(takeOff).Centre;
        f.Next(at, out _);
        Assert.Equal(NavEdgeType.Jump, f.NextVia);
        float z = 0f, vz = 289f, x = at.X;
        for (int t = 0; t < 60; t++)
        {
            f.Next(new AiVector(x, at.Y, z), out _);
            z = MathF.Max(0f, z + (vz * 0.015f));
            vz = z > 0f ? vz - 12f : 0f;
            x += 3.6f;
        }

        Assert.Equal(0, f.HopsFailed);
    }

    /// <summary>A jump down across a gap (take-off z 52, landing z 0 96 u along) that lands on its cell: no failure, though the body passes the take-off's height over the gap.</summary>
    [Fact]
    public void ADownwardGapJumpThatLandsIsNoFailure()
    {
        var b = new NavGraphBuilder();
        CellId takeOff = Cell(b, 0f, 0f, 52f);
        CellId landing = Cell(b, 96f, 0f, 0f);
        b.AddEdge(takeOff, landing, NavEdgeType.Jump);
        NavGraph g = b.Build();
        NavFollower f = Toward(g, landing);
        AiVector at = g.Cell(takeOff).Centre;
        f.Next(at, out _);
        Assert.Equal(NavEdgeType.Jump, f.NextVia);
        float z = 52f, vz = 289f, x = at.X;
        for (int t = 0; t < 70; t++)
        {
            f.Next(new AiVector(x, at.Y, z), out _);
            z = MathF.Max(x >= 96f ? 0f : -400f, z + (vz * 0.015f));
            vz -= 12f;
            x = MathF.Min(x + 1.7f, 104f);
        }

        Assert.Equal(0, f.HopsFailed);
    }
}
