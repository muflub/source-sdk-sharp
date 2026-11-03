using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Predict;

/// <summary>
/// The position predictor (plan_hl2mp_ai §5.3.1) with the plan's numbers: an
/// opponent who always walks corridor A then B has at least 70 % of the mass
/// on B two seconds after sight is lost at A's end, against at most 35 % with
/// a flat prior; a heard shot moves the arg-max at once; a cell seen empty
/// holds nothing.
/// </summary>
public sealed class PositionPredictorTests
{
    private const int Len = 15;

    // A runs east along row Len to the junction at (Len, Len); B goes north
    // from it, C south.
    private static (NavGraph Graph, int[,] Ids) TJunction()
    {
        var rows = new string[2 * Len + 1];
        for (int y = 0; y < rows.Length; y++)
            rows[y] = y == Len ? new string('.', Len + 1) : new string(' ', Len) + ".";
        return Grids.Parse(rows);
    }

    private static readonly (NavGraph Graph, int[,] Ids) G = TJunction();

    private static bool OnB(int cell) => G.Graph.Cell(cell).MinY > G.Graph.Cell(G.Ids[Len, Len]).MinY;

    private static float AfterTwoSeconds(bool learned)
    {
        var prior = new byte[G.Graph.CellCount];
        if (learned)
        {
            for (int x = 0; x <= Len; x++)
                prior[G.Ids[x, Len]] = 20;
            for (int y = 0; y < Len; y++)
                prior[G.Ids[Len, y]] = 20;
        }

        // The baseline is the flat prior: no route, and so no way it was going either.
        var p = new PositionPredictor(G.Graph);
        p.LostAt(Grids.At(G.Ids, Len - 1, Len), learned ? new AiVector(200, 0, 0) : default);
        for (int t = 0; t < 20; t++)
            p.Step(0.1f, 200f, prior);
        return p.MassOn(OnB);
    }

    [Fact]
    public void TheLearnedRoutePutsMostOfTheMassOnB()
    {
        float b = AfterTwoSeconds(learned: true);
        Assert.True(b >= 0.70f, $"mass on B {b:P1}");
    }

    [Fact]
    public void AFlatPriorPutsLittleOfItOnB()
    {
        float b = AfterTwoSeconds(learned: false);
        Assert.True(b <= 0.35f, $"mass on B {b:P1}");
    }

    [Fact]
    public void AHeardShotMovesTheArgMaxAtOnce()
    {
        var p = new PositionPredictor(G.Graph);
        p.LostAt(Grids.At(G.Ids, 2, Len), default);
        p.Step(0.5f, 200f, []);
        CellId heard = Grids.At(G.Ids, Len, 3);
        p.Heard(heard, 40f);
        Assert.Equal(heard, p.ArgMax);
    }

    [Fact]
    public void ACellSeenEmptyHoldsNothing()
    {
        var p = new PositionPredictor(G.Graph);
        p.LostAt(Grids.At(G.Ids, Len - 1, Len), default);
        p.Step(0.3f, 200f, []);
        int seen = G.Ids[Len, Len];
        Assert.True(p.P(seen) > 0f, "the control: the junction held mass");
        p.SeenEmpty(seen);
        Assert.Equal(0f, p.P(seen));
        Assert.Equal(1f, p.MassOn(_ => true), 3);
    }

    [Fact]
    public void TheEntropyGrowsAsTheMassSpreads()
    {
        var p = new PositionPredictor(G.Graph);
        p.LostAt(Grids.At(G.Ids, Len - 1, Len), default);
        Assert.Equal(0f, p.Entropy);
        p.Step(1f, 200f, []);
        Assert.True(p.Entropy > 1f);
    }

    // Live, dm_lockdown (2026-11-02): 16 beliefs spread over the whole map
    // cost the inline brains 13 ms a tick. The mass stays on at most
    // MaxCells cells and stops spreading MaxSpreadSeconds after the loss.
    [Fact]
    public void TheBeliefStaysOnABoundedSetOfCellsAndStopsSpreading()
    {
        (NavGraph g, int[,] ids) = Grids.Parse([.. Enumerable.Range(0, 80).Select(_ => new string('.', 80))]);
        var p = new PositionPredictor(g);
        p.LostAt(Grids.At(ids, 40, 40), default);
        for (int t = 0; t < 90; t++)
            p.Step(0.1f, 4000f, []);
        Assert.True(p.CellsWithMass <= 2048, $"{p.CellsWithMass} cells");
        Assert.True(p.CellsWithMass > 500, "the control: it spread");
        int after = p.CellsWithMass;
        float entropy = p.Entropy;
        for (int t = 0; t < 40; t++)
            p.Step(0.1f, 4000f, []);
        Assert.Equal((after, entropy), (p.CellsWithMass, p.Entropy));
        Assert.Equal(1f, p.MassOn(_ => true), 3);
    }
}
