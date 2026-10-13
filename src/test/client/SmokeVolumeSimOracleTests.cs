using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The smoke volume's simulation against stock <c>C_FuncSmokeVolume</c>,
/// recorded live (<c>Fixtures/smokevolume-stock.tsv</c>, README beside it):
/// the grid fill with every world answer it took, then 60 frames of trades,
/// density ramp and spin, every cell's state and every quad. Bit-exact: the
/// draws and world answers are replayed with their inputs compared as bits.
/// </summary>
public class SmokeVolumeSimOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

    private static Vector V(string[] c, int at) => new(F(c[at]), F(c[at + 1]), F(c[at + 2]));

    private static bool Same(Vector a, Vector b) => Bits(a.X) == Bits(b.X) && Bits(a.Y) == Bits(b.Y) && Bits(a.Z) == Bits(b.Z);

    private sealed class Frame
    {
        public required string[] Head;
        public string[]? Bounds;
        public readonly List<string[]> Draws = new();
        public readonly List<string[]> Cells = new();
        public string[]? Step;
        public readonly List<string[]> Quads = new();
    }

    private sealed class Fill
    {
        public required string[] Cell;
        public readonly List<string[]> Draws = new();
        public string[]? Light;
        public readonly List<string[]> Traces = new();
        public string[]? Result;
    }

    private sealed record Oracle(string[] Volume, string[] Grid, List<Fill> Fills, List<Frame> Frames);

    private static Oracle Stock()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "smokevolume-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock smoke volume is missing: " + path);

        string[]? volume = null, grid = null;
        var fills = new List<Fill>();
        var frames = new List<Frame>();
        Fill? fill = null;
        Frame? frame = null;

        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] c = line.Split('\t');
            switch (c[0])
            {
                case "V": volume = c; break;
                case "N": grid = c; break;
                case "C": fill = new Fill { Cell = c }; fills.Add(fill); break;
                case "L": fill!.Light = c; break;
                case "T": fill!.Traces.Add(c); break;
                case "K": fill!.Result = c; break;
                case "F": frame = new Frame { Head = c }; frames.Add(frame); fill = null; break;
                case "B": frame!.Bounds = c; break;
                case "RI": case "RF":
                    if (frame is not null) frame.Draws.Add(c); else fill!.Draws.Add(c);
                    break;
                case "G": frame!.Cells.Add(c); break;
                case "S": frame!.Step = c; break;
                case "D": frame!.Quads.Add(c); break;
                default: Assert.Fail($"an unknown row kind '{c[0]}' in the smoke volume oracle"); break;
            }
        }

        Assert.True(volume is not null && grid is not null && fills.Count(f => f.Result is not null) > 20
                    && frames.Count >= 30 && frames.Sum(f => f.Cells.Count) > 1000,
            $"the oracle parsed ({fills.Count} cells, {frames.Count} frames)");
        return new Oracle(volume!, grid!, fills, frames);
    }

    private sealed class Replay : IParticleRandom
    {
        public readonly Queue<string[]> Pending = new();

        public float RandomFloat(float min, float max)
        {
            var c = Next("RF");
            Assert.True(Bits(F(c[1])) == Bits(min) && Bits(F(c[2])) == Bits(max), $"RandomFloat bounds ({c[1]}, {c[2]}) vs ({min:R}, {max:R})");
            return F(c[3]);
        }

        public int RandomInt(int min, int max)
        {
            var c = Next("RI");
            Assert.True(I(c[1]) == min && I(c[2]) == max, $"RandomInt bounds ({c[1]}, {c[2]}) vs ({min}, {max})");
            return I(c[3]);
        }

        private string[] Next(string kind)
        {
            Assert.True(Pending.Count > 0, $"a {kind} draw stock did not take");
            var c = Pending.Dequeue();
            Assert.True(c[0] == kind, $"stock drew {c[0]} where the port drew {kind}");
            return c;
        }
    }

    /// <summary>The world as the fill saw it: each cell's contents, light and six traces, asked in stock's order.</summary>
    private sealed class World(List<Fill> fills, Replay random) : SmokeVolumeSim.IWorld
    {
        private int _cell = -1;
        private int _trace;

        public int PointContents(Vector position)
        {
            _cell++;
            var fill = fills[_cell];
            Assert.True(Same(position, V(fill.Cell, 5)), $"cell {_cell} asked at {position.X:R} {position.Y:R} {position.Z:R}; stock {fill.Cell[5]} {fill.Cell[6]} {fill.Cell[7]}");
            foreach (var d in fill.Draws)
                random.Pending.Enqueue(d);
            _trace = 0;
            return I(fill.Cell[4]);
        }

        public Vector Light(Vector position)
        {
            var fill = fills[_cell];
            Assert.True(fill.Light is not null, $"cell {_cell}: a light stock did not sample");
            return V(fill.Light!, 1);
        }

        public (float Fraction, Vector Normal, float PlaneDist) Trace(Vector start, Vector end)
        {
            var t = fills[_cell].Traces[_trace];
            Assert.True(I(t[1]) == _trace, $"cell {_cell}: trace {_trace} out of order");
            _trace++;
            return (F(t[2]), V(t, 3), F(t[6]));
        }
    }

    private static string Row(IEnumerable<float> values, params int[] ints)
        => string.Join(",", values.Select(v => Bits(v).ToString("x8", CultureInfo.InvariantCulture))
                                  .Concat(ints.Select(i => i.ToString(CultureInfo.InvariantCulture))));

    private static (SmokeVolumeSim Sim, Replay Random) Filled(Oracle oracle)
    {
        var v = oracle.Volume;
        var sim = new SmokeVolumeSim
        {
            VolumeMins = V(v, 2), VolumeMaxs = V(v, 5),
            ParticleDrawWidth = F(v[11]), ParticleSpacingDistance = F(v[12]), DensityRampSpeed = F(v[13]),
            RotationSpeed = F(v[14]), MovementSpeed = F(v[15]),
            Red1 = (byte)I(v[16]), Green1 = (byte)I(v[17]), Blue1 = (byte)I(v[18]),
            Red2 = (byte)I(v[19]), Green2 = (byte)I(v[20]), Blue2 = (byte)I(v[21]),
            SpawnFlags = I(v[22]),
            Density = F(oracle.Grid[6]),
        };
        var random = new Replay();
        sim.ApplyUpdate(created: true, reduceParticles: v[23] != "0", new World(oracle.Fills, random), random);
        return (sim, random);
    }

    [Fact]
    public void TheGridFillsAsStocksDid()
    {
        var oracle = Stock();
        var (sim, random) = Filled(oracle);

        var g = oracle.Grid;
        Assert.True(sim.XCount == I(g[1]) && sim.YCount == I(g[2]) && sim.ZCount == I(g[3]), $"grid {sim.XCount}x{sim.YCount}x{sim.ZCount}");
        Assert.True(Bits(sim.SpacingRadius) == Bits(F(g[4])) && Bits(sim.ParticleRadius) == Bits(F(g[5])), "the radii");
        Assert.True(random.Pending.Count == 0, "every fill draw was taken");

        // The cells with a puff, in fill order: their colour, fade and the puff's start.
        var results = oracle.Fills.Where(f => f.Result is not null).ToList();
        var filled = sim.Cells.ToArray().Where(c => c.Puff >= 0).ToList();
        Assert.True(results.Count == filled.Count, $"stock filled {results.Count} cells, the port {filled.Count}");

        // The fill walks x, then y, then z innermost; the cells are indexed z-major. Compare by the puff each holds.
        var byPuff = filled.OrderBy(c => c.Puff).ToList();
        for (int k = 0; k < results.Count; k++)
        {
            var want = results[k].Result!;
            var cell = byPuff[k];
            var puff = sim.Puffs[cell.Puff];
            Assert.True(Row([cell.FadeAlpha, puff.RotationFactor, puff.CurRotation], cell.R, cell.G, cell.B, puff.ColorInterp)
                        == Row([F(want[4]), F(want[6]), F(want[7])], I(want[1]), I(want[2]), I(want[3]), I(want[5])),
                $"filled cell {k}: stock {string.Join(" ", want.Skip(1))}");
        }
    }

    [LinuxOnlyFact("a golden recorded bit for bit against glibc's libm; the Windows C runtime rounds the last bit of sin, cos and pow differently")]
    public void SixtyFramesReplayBitForBit()
    {
        var oracle = Stock();
        var (sim, random) = Filled(oracle);
        int cells = 0, quads = 0, trading = 0;
        bool reduce = oracle.Volume[23] != "0";

        for (int f = 0; f < oracle.Frames.Count; f++)
        {
            var frame = oracle.Frames[f];
            Assert.True(Bits(sim.CurrentDensity) == Bits(F(frame.Head[8])), $"frame {f}: density on entry {sim.CurrentDensity:R}, stock {frame.Head[8]}");

            foreach (var d in frame.Draws)
                random.Pending.Enqueue(d);

            // The recording's box is already world-space (README), so the origin it
            // is moved by is zero; the stock rule (mins + origin) is unchanged.
            sim.Update(F(frame.Head[1]), default, V(frame.Head, 5), random, out var bounds);
            Assert.True(random.Pending.Count == 0, $"frame {f}: {random.Pending.Count} draw(s) untaken");
            Assert.True((bounds is null) == (frame.Bounds is null), $"frame {f}: bounds set {bounds is not null}");
            if (bounds is { } b)
                Assert.True(Same(b.Min, V(frame.Bounds!, 1)) && Same(b.Max, V(frame.Bounds!, 4)), $"frame {f}: the bounds differ");

            var cellArray = sim.Cells.ToArray();
            foreach (var g in frame.Cells)
            {
                var cell = cellArray[I(g[1])];
                Assert.True(cell.Puff >= 0, $"frame {f}: cell {g[1]} holds no puff in the port");
                var puff = sim.Puffs[cell.Puff];
                bool traded = cell.TradeIndex != -1;
                Assert.True(Row([traded ? cell.TradeClock : 0f, traded ? cell.TradeDuration : 0f,
                                 puff.Position.X, puff.Position.Y, puff.Position.Z, puff.FadeAlpha, puff.RotationFactor, puff.CurRotation],
                                cell.TradeIndex, puff.R, puff.G, puff.B, puff.ColorInterp)
                            == Row([F(g[3]), F(g[4]), F(g[5]), F(g[6]), F(g[7]), F(g[8]), F(g[12]), F(g[13])],
                                   I(g[2]), I(g[9]), I(g[10]), I(g[11]), I(g[14])),
                    $"frame {f}: cell {g[1]} differs (stock {string.Join(" ", g.Skip(2))})");
                cells++;
                if (traded)
                    trading++;
            }
            Assert.True(frame.Cells.Count == cellArray.Count(c => c.Puff >= 0), $"frame {f}: cell count");

            if (frame.Step is { } step)
                Assert.True(sim.Simulate(F(step[1])), $"frame {f}: stock simulated");

            if (frame.Quads.Count > 0)
            {
                var want = frame.Quads.Where(q => q[10] == "1")
                    .Select(q => Row(q.Skip(1).Take(9).Select(F))).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var have = sim.Puffs.Select(p => sim.Draw(p, reduce)).Where(q => q is not null).Select(q => q!.Value)
                    .Select(q => Row([q.Position.X, q.Position.Y, q.Position.Z, q.Color.X, q.Color.Y, q.Color.Z, q.Alpha, q.Size, q.Roll]))
                    .OrderBy(x => x, StringComparer.Ordinal).ToList();
                Assert.True(want.SequenceEqual(have), $"frame {f}: quads differ; first stock '{want.FirstOrDefault()}', port '{have.FirstOrDefault()}'");
                quads += want.Count;
            }
        }

        Assert.True(cells > 1000 && quads > 1000 && trading > 100,
            $"the replay reached its subject ({cells} cell states, {quads} quads, {trading} trading)");
    }

    [Fact]
    public void AReloadCarriesTheGridAndItsTrades()
    {
        var oracle = Stock();
        var (sim, random) = Filled(oracle);
        foreach (var frame in oracle.Frames.Take(30))
        {
            foreach (var d in frame.Draws)
                random.Pending.Enqueue(d);
            sim.Update(F(frame.Head[1]), default, V(frame.Head, 5), random, out _);
            if (frame.Step is { } step)
                sim.Simulate(F(step[1]));
        }

        using var buffer = new MemoryStream();
        sim.Save(new BinaryWriter(buffer));
        byte[] carried = buffer.ToArray();

        var fresh = new SmokeVolumeSim();
        Assert.True(fresh.Puffs.Count == 0 && sim.Puffs.Count > 20, "a fresh sim has no grid");
        fresh.Restore(new BinaryReader(new MemoryStream(carried)));
        Assert.True(fresh.Cells.ToArray().Count(c => c.TradeIndex != -1 && c.Puff >= 0) == sim.Cells.ToArray().Count(c => c.TradeIndex != -1 && c.Puff >= 0)
                    && sim.Cells.ToArray().Any(c => c.TradeIndex != -1 && c.Puff >= 0),
            "the trades in flight came across");

        sim.Simulate(0.0166f);
        fresh.Simulate(0.0166f);
        Assert.True(sim.Puffs.Select(p => Bits(p.CurRotation)).SequenceEqual(fresh.Puffs.Select(p => Bits(p.CurRotation))),
            "the restored grid spins as the original does");

        using var again = new MemoryStream();
        var copy = new SmokeVolumeSim();
        copy.Restore(new BinaryReader(new MemoryStream(carried)));
        copy.Save(new BinaryWriter(again));
        Assert.True(again.ToArray().AsSpan().SequenceEqual(carried), "a restored copy saves the same bytes");
    }
}
