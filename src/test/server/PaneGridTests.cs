using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The pane grid of a func_breakable_surf.
///
/// <para>
/// This is the arithmetic the surface was kept off the parallel hierarchy for,
/// and the reason it is worth having here rather than in C++ is exactly this
/// file: the alternative way to find out whether support propagates correctly is
/// to shoot a window and watch it.
/// </para>
///
/// <para>
/// The numbers are stock's. A window that comes apart differently from the one
/// every Half-Life 2 map was built around is a divergence nobody asked for, so
/// the weights are pinned rather than described.
/// </para>
/// </summary>
public class PaneGridTests
{
    /// <summary>
    /// A basis for a surface facing -X: right is +Y, up is +Z.
    ///
    /// Stands in for the engine's VectorAngles/AngleVectors, which the real path
    /// calls so that the client placing panes and the server breaking them
    /// cannot disagree. What is under test here is the grid, not the trigonometry.
    /// </summary>
    private static (Vector, Vector) FacingBasis(Vector forward)
        => (new Vector(0f, 1f, 0f), new Vector(0f, 0f, 1f));

    /// <summary>
    /// A 48x36 pane of glass in the YZ plane, facing -X. Four panes wide, three
    /// high, at exactly twelve units each.
    /// </summary>
    private static PaneGrid.Quad Window() => new(
        LowerLeft:  new Vector(0f,  0f,  0f),
        LowerRight: new Vector(0f, 48f,  0f),
        UpperLeft:  new Vector(0f,  0f, 36f),
        UpperRight: new Vector(0f, 48f, 36f));

    private static PaneGrid Grid(Vector attackDir = default)
        => PaneGrid.Derive(Window(), new Vector(-1f, 0f, 0f), attackDir, FacingBasis);

    /// <summary>
    /// Which panes a shot takes out.
    ///
    /// <para>
    /// The extras are what make a bullet hole in glass look like one rather than
    /// a missing square, and they are the part a port is most likely to leave
    /// out — the pane the shot hit is obvious and the rest is not.
    /// </para>
    /// </summary>
    [Fact]
    public void Shots()
    {
        // Tails, tails: no upward spread, so only the geometry shows.
        static Func<int> Tails() => () => 1;

        var grid = Grid();
        var middle = grid.PanesHitAt(1.5f, 1.5f, spreads: true, coin: Tails());

        Assert.True(middle.Count == 1 && middle[0] == (1, 1), "a shot in the middle of a pane takes that pane");

        // Near a side, the pane it was nearly in goes too. 0.9 is past stock's
        // 0.8; 0.5 would not be.
        var edge = Grid().PanesHitAt(1.9f, 1.5f, spreads: true, coin: Tails());
        Assert.True(edge.Count == 2 && edge.Contains((1, 1)) && edge.Contains((2, 1)), "a shot near a pane's edge takes the neighbour");

        var lowEdge = Grid().PanesHitAt(1.1f, 1.5f, spreads: true, coin: Tails());
        Assert.True(lowEdge.Contains((1, 1)) && lowEdge.Contains((0, 1)), "and the other side too");

        // The frame is not a pane: a shot at the outer edge has no neighbour
        // that way and must not report one.
        var atFrame = Grid().PanesHitAt(0.1f, 1.5f, spreads: true, coin: Tails());
        Assert.True(atFrame.All(p => p.W >= 0 && p.H >= 0), "a shot at the frame takes nothing outside the grid");

        // Heads: upward. Heads again: one more above that.
        int flips = 0;
        var upward = Grid().PanesHitAt(1.5f, 0.5f, spreads: true, coin: () => flips++ < 2 ? 0 : 1);
        Assert.True(upward.Contains((1, 0)) && upward.Contains((1, 1)) && upward.Contains((1, 2)), "a coin flip runs the damage upward");

        // Tile loses exactly the piece that was hit.
        var tile = Grid().PanesHitAt(1.9f, 1.9f, spreads: false, coin: () => 0);
        Assert.True(tile.Count == 1 && tile[0] == (1, 1), "tile loses only the piece that was hit");

        // A pane already gone is not reported again, which is what stops a
        // second shot in the same place counting as damage.
        var twice = Grid();
        twice.Break(1, 1);
        Assert.True(!twice.PanesHitAt(1.5f, 1.5f, spreads: true, coin: Tails()).Contains((1, 1)), "an already-broken pane is not taken again");
    }

    [Fact]
    public void Geometry()
    {
        var grid = Grid();

        Assert.True(grid.Wide == 4 && grid.High == 3, "a 48x36 face is four panes by three");

        // The pane size divides the face evenly rather than being fixed at
        // twelve, so a window is never left with a sliver at the end.
        Assert.True(MathF.Abs(grid.PanelWidth - 12f) < 0.001f
              && MathF.Abs(grid.PanelHeight - 12f) < 0.001f, "panes divide the face evenly");

        // 50 / 12 is four and a bit; the remainder widens the panes rather than
        // adding a fifth column.
        var odd = PaneGrid.Derive(new PaneGrid.Quad(
                new Vector(0f, 0f, 0f), new Vector(0f, 50f, 0f),
                new Vector(0f, 0f, 36f), new Vector(0f, 50f, 36f)),
            new Vector(-1f, 0f, 0f), default, FacingBasis);

        Assert.True(odd.Wide == 4 && MathF.Abs(odd.PanelWidth - 12.5f) < 0.001f, "a face that does not divide evenly widens its panes");

        // A wall far bigger than the grid can hold is clamped rather than
        // overflowing the 16x16 the wire carries.
        var huge = PaneGrid.Derive(new PaneGrid.Quad(
                new Vector(0f, 0f, 0f), new Vector(0f, 4000f, 0f),
                new Vector(0f, 0f, 4000f), new Vector(0f, 4000f, 4000f)),
            new Vector(-1f, 0f, 0f), default, FacingBasis);

        Assert.True(huge.Wide == PaneGrid.MaxPanels && huge.High == PaneGrid.MaxPanels, "a huge face is clamped to the grid the wire carries");

        // The side that shatters is the side the blow came from, and the way that
        // shows is the CORNER: the whole quad steps one unit towards the
        // attacker, so the broken face sits where the shooter is standing.
        var fromBehind = Grid(new Vector(1f, 0f, 0f));
        var fromFront  = Grid(new Vector(-1f, 0f, 0f));

        Assert.True(MathF.Abs(fromBehind.Corner.X - fromFront.Corner.X + 1f) < 0.001f, "a blow from behind moves the face to that side");

        // And the normal does NOT flip, which is worth pinning because it looks
        // like a bug and is not: it is recomputed from the face's own edges
        // after the branch, so both sides end up facing the same way. Stock does
        // exactly this.
        Assert.True(MathF.Abs(fromBehind.Normal.X - fromFront.Normal.X) < 0.001f, "the normal comes from the face, not from the blow");

        Assert.True(!grid.IsBroken(0, 0) && !grid.IsBroken(3, 2) && grid.BrokenCount == 0, "every pane starts whole");

        // Out of range reads as broken, which is what lets the support
        // arithmetic treat the world past the window as nothing to lean on.
        Assert.True(grid.IsBroken(-1, 0) && grid.IsBroken(4, 0) && grid.IsBroken(0, 3), "outside the grid reads as broken");
    }

    [Fact]
    public void Supports()
    {
        var grid = Grid();

        // A pane in the middle of an intact window, held on all eight sides:
        // 0.01 + 1 + 1.25 + 1 + 1 + 1 + 1 + 0.25 + 0.25.
        Assert.True(MathF.Abs(grid.RecalcSupport(1, 1) - 6.76f) < 0.001f, "an intact pane is held on every side");

        // The weights are NOT symmetrical, and that asymmetry is the whole
        // character of the effect: what is below counts for more than what is
        // above, so a window comes apart downwards.
        var below = Grid();
        below.Break(1, 0);
        float lostBelow = grid.RecalcSupport(1, 1) - below.RecalcSupport(1, 1);

        var above = Grid();
        above.Break(1, 2);
        float lostAbove = grid.RecalcSupport(1, 1) - above.RecalcSupport(1, 1);

        Assert.True(lostBelow > lostAbove, "losing the pane below costs more than losing the one above");
        Assert.True(MathF.Abs(lostBelow - 1.25f) < 0.001f, "the pane below is worth 1.25");
        Assert.True(MathF.Abs(lostAbove - 1.0f) < 0.001f, "the pane above is worth 1.0");

        // The frame holds the edges up, so a corner is not weaker than the
        // middle just for having fewer neighbours.
        Assert.True(MathF.Abs(grid.RecalcSupport(0, 0) - grid.RecalcSupport(1, 1)) < 0.001f, "the frame counts as a neighbour");

        Assert.True(Broken(grid).Support(1, 1) == 0f, "a broken pane holds nothing up");
    }

    private static PaneGrid Broken(PaneGrid _)
    {
        var grid = Grid();
        grid.Break(1, 1);
        return grid;
    }

    [Fact]
    public void Settling()
    {
        // An intact window is stable: nothing falls out of one nobody touched.
        var grid = Grid();
        Assert.True(grid.Settle(100).Count == 0, "an intact window settles with nothing falling");

        // One hole is not enough to bring anything else down either, which is
        // what makes a bullet hole a bullet hole rather than the end of the
        // window.
        grid.Break(1, 1);
        Assert.True(grid.Settle(100).Count == 0, "a single hole does not spread");

        // Take out everything around a pane and it has nothing left but the
        // 0.01 floor and whatever the frame gives it.
        var isolated = Grid();
        isolated.Break(0, 1);
        isolated.Break(2, 1);
        isolated.Break(1, 0);
        isolated.Break(1, 2);
        isolated.Break(0, 0);
        isolated.Break(2, 0);
        isolated.Break(0, 2);
        isolated.Break(2, 2);

        Assert.True(isolated.Settle(100).Contains((1, 1)), "a pane with nothing around it falls");

        // Fragility scales the threshold, so tougher glass survives a state that
        // ordinary glass would not. A pane hanging by the one above it has
        // support 1.01 of a possible 6.75 — under stock's 0.20 and over the 0.10
        // that half-fragility asks for.
        Assert.True(HangingByTheTop().Settle(100).Contains((1, 1)), "ordinary glass lets go");
        Assert.True(!HangingByTheTop().Settle(50).Contains((1, 1)), "tougher glass holds on");

        // Settle is TWO passes on purpose: a pane weakened in this sweep must
        // not weaken its neighbour in the same one, or the whole sheet unzips
        // in a single tick instead of coming apart over several.
        var sheet = Grid();
        sheet.Break(0, 0);
        int firstSweep = sheet.Settle(100).Count;
        Assert.True(firstSweep < 4 * 3, "one sweep does not cascade through the sheet");

        Assert.True(Grid().BreakAll().Count == 4 * 3, "breaking every pane reports every pane");

        var counted = Grid();
        counted.BreakAll();
        Assert.True(counted.BrokenCount == 12, "and counts them");

        Assert.True(!counted.Break(0, 0), "breaking a pane twice counts it once");
    }

    /// <summary>Pane (1,1) with every neighbour gone but the one above it.</summary>
    private static PaneGrid HangingByTheTop()
    {
        var grid = Grid();

        grid.Break(1, 0); grid.Break(0, 1); grid.Break(2, 1);
        grid.Break(0, 0); grid.Break(2, 0);
        grid.Break(0, 2); grid.Break(2, 2);

        return grid;
    }

    [Fact]
    public void Positions()
    {
        var grid = Grid();

        // The corner is the origin of the pane counting, so it is pane (0,0).
        var (w, h) = grid.PanePos(grid.Corner);
        Assert.True(MathF.Abs(w) < 0.001f && MathF.Abs(h) < 0.001f, "the corner is the first pane");

        // A point one pane along each axis lands on pane (1,1) — this is what
        // turns a bullet's impact position into the pane it broke, and getting
        // it wrong shatters the wrong piece of glass.
        var middle = grid.Corner + new Vector(0f, 12f, 12f);
        var (mw, mh) = grid.PanePos(middle);
        Assert.True(MathF.Abs(mw - 1f) < 0.001f && MathF.Abs(mh - 1f) < 0.001f, "a point one pane along is the next pane");

        Assert.True(RoundTrips(grid, 2, 1) && RoundTrips(grid, 3, 2) && RoundTrips(grid, 0, 0), "a pane's origin round-trips through PanePos");
    }

    private static bool RoundTrips(PaneGrid grid, int w, int h)
    {
        var (backW, backH) = grid.PanePos(grid.PaneOrigin(w, h));
        return MathF.Abs(backW - w) < 0.001f && MathF.Abs(backH - h) < 0.001f;
    }
}
