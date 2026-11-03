using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Predict;

/// <summary>
/// The position predictor against the truth (plan_hl2mp_ai §8.2, prediction
/// accuracy): an opponent walks a loop of corridors, always the same way,
/// seen only along one side. While unseen for at most 3 s the predictor's
/// arg-max is within 2 cells of it at least 60 % of the decides, the flat
/// baseline (no route prior, no direction) at most 40 %.
/// </summary>
public sealed class PositionAccuracyTests
{
    private const int Side = 12;

    // A square loop of corridors, Side cells a side.
    private static readonly (NavGraph Graph, int[,] Ids) G = Grids.Parse([.. Enumerable.Range(0, Side).Select(y =>
        y == 0 || y == Side - 1 ? new string('.', Side) : "." + new string(' ', Side - 2) + ".")]);

    /// <summary>The loop's cells in walking order, clockwise from the top-left.</summary>
    private static List<(int X, int Y)> Loop()
    {
        var l = new List<(int, int)>();
        for (int x = 0; x < Side; x++) l.Add((x, 0));
        for (int y = 1; y < Side; y++) l.Add((Side - 1, y));
        for (int x = Side - 2; x >= 0; x--) l.Add((x, Side - 1));
        for (int y = Side - 2; y > 0; y--) l.Add((0, y));
        return l;
    }

    private static float Score(bool learned)
    {
        List<(int X, int Y)> loop = Loop();
        NavGraph g = G.Graph;
        var prior = new byte[g.CellCount];
        if (learned)
        {
            foreach ((int x, int y) in loop)
                prior[G.Ids[x, y]] = 10;
        }

        var p = new PositionPredictor(g);
        const float speed = 190f, dt = 0.1f;
        float pos = 0f;
        int unseenDecides = 0, near = 0;
        bool wasSeen = true;
        float unseenFor = 0f;
        for (int step = 0; step < 600; step++)
        {
            pos += speed * dt / Grids.Size;
            int i = (int)pos % loop.Count;
            (int x, int y) = loop[i];
            bool seen = y == 0;   // the top corridor is in the bot's sight
            if (!seen && wasSeen)
            {
                (int px, int py) = loop[(i - 1 + loop.Count) % loop.Count];
                var vel = new AiVector((x - px) * speed, (py - y) * speed, 0f);
                p.LostAt(new CellId(G.Ids[px, py]), learned ? vel : default);
                unseenFor = 0f;
            }

            if (!seen)
            {
                p.Step(dt, speed, prior);
                unseenFor += dt;
                if (unseenFor <= 3f)
                {
                    unseenDecides++;
                    CellId arg = p.ArgMax;
                    if (arg.IsValid && g.Cell(arg).Centre.DistanceTo(g.Cell(G.Ids[x, y]).Centre) <= 2.01f * Grids.Size)
                        near++;
                }
            }

            wasSeen = seen;
        }

        Assert.True(unseenDecides > 50, "the control: the opponent was unseen");
        return near / (float)unseenDecides;
    }

    [Fact]
    public void TheArgMaxFollowsALearnedRouteMostOfTheTime()
    {
        float s = Score(learned: true);
        Assert.True(s >= 0.60f, $"within 2 cells {s:P1}");
    }

    [Fact]
    public void TheFlatBaselineDoesNot()
    {
        float s = Score(learned: false);
        Assert.True(s <= 0.40f, $"within 2 cells {s:P1}");
    }
}
