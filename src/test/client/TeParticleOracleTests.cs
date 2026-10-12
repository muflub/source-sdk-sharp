using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The legacy temp entities' particles against stock (lane c5-particles):
/// TE_BloodStream's and the debug line's particles, CTEParticleRenderer's step
/// and its render, as <c>Fixtures/teparticles-stock.tsv.gz</c> recorded them
/// (README beside it). Bit for bit, with the recorded draws fed back.
/// </summary>
public class TeParticleOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static Vector V(string[] r, int at) => new(F(r[at]), F(r[at + 1]), F(r[at + 2]));

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static bool Same(Vector a, Vector b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

    private static readonly Lazy<List<string[]>> s_rows = new(() =>
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "teparticles-stock.tsv.gz");
        Assert.True(File.Exists(path), path + " is missing; a replay of nothing would pass");
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        var rows = new List<string[]>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && line[0] != '#')
                rows.Add(line.Split('\t'));
        }
        return rows;
    });

    private static List<string[]> Rows => s_rows.Value;

    private sealed class Recorded(IEnumerable<string[]> draws) : IParticleRandom
    {
        private readonly Queue<string[]> _q = new(draws);
        public readonly List<string> Wrong = [];

        public int Remaining => _q.Count;

        public float RandomFloat(float min, float max)
        {
            if (!_q.TryDequeue(out var r) || r[1] != "f" || !Same(min, F(r[2])) || !Same(max, F(r[3])))
            {
                Wrong.Add($"drew float [{min:G9}, {max:G9}], stock {(r is null ? "none" : string.Join(' ', r))}");
                return min;
            }
            return F(r[4]);
        }

        public int RandomInt(int min, int max)
        {
            if (!_q.TryDequeue(out var r) || r[1] != "i" || min != I(r[2]) || max != I(r[3]))
            {
                Wrong.Add($"drew int [{min}, {max}], stock {(r is null ? "none" : string.Join(' ', r))}");
                return min;
            }
            return I(r[4]);
        }
    }

    /// <summary>A particle's columns from <c>tpp</c>/<c>tpi</c>/<c>tpo</c>: position at 3, as the probe's state row writes them.</summary>
    private static TeParticle State(string[] r) => new()
    {
        Position = V(r, 3), Velocity = V(r, 6), Lifetime = F(r[9]), Type = (TeParticleType)I(r[10]),
        Word = (ushort)I(r[11]), R = (byte)I(r[12]), G = (byte)I(r[13]), B = (byte)I(r[14]), A = (byte)I(r[15]),
    };

    private static string Diff(in TeParticle p, string[] stock)
        => $"{p.Position.X:G9} {p.Position.Y:G9} {p.Position.Z:G9} v {p.Velocity.X:G9} {p.Velocity.Y:G9} {p.Velocity.Z:G9} " +
           $"life {p.Lifetime:G9} type {(int)p.Type} word {p.Word} rgba {p.R} {p.G} {p.B} {p.A}; stock {string.Join(' ', stock)}";

    private static bool Equal(in TeParticle p, in TeParticle s)
        => Same(p.Position, s.Position) && Same(p.Velocity, s.Velocity) && Same(p.Lifetime, s.Lifetime) && p.Type == s.Type
           && p.Word == s.Word && p.R == s.R && p.G == s.G && p.B == s.B && p.A == s.A;

    [Fact]
    public void EveryEffectMakesStocksParticles()
    {
        var wrong = new List<string>();
        int effects = 0, particles = 0, blood = 0, lines = 0;
        for (int at = 0; at < Rows.Count; at++)
        {
            string[] e = Rows[at];
            if (e[0] != "tbe" && e[0] != "tse")
                continue;
            int end = at + 1;
            while (end < Rows.Count && (Rows[end][0] == "tpr" || Rows[end][0] == "tpp"))
                end++;
            var unit = Rows.GetRange(at + 1, end - at - 1);
            var rec = new Recorded(unit.Where(r => r[0] == "tpr"));
            List<TeParticle> made;
            if (e[0] == "tbe")
            {
                blood++;
                made = TeParticles.BloodStream(V(e, 2), V(e, 5), I(e[8]), I(e[9]), I(e[10]), I(e[11]), I(e[12]), rec);
            }
            else
            {
                lines++;
                made = TeParticles.ShowLine(V(e, 2), V(e, 5));
            }
            effects++;

            var stock = unit.Where(r => r[0] == "tpp").ToList();
            if (made.Count != stock.Count)
                wrong.Add($"{e[0]} {effects}: {made.Count} particle(s), stock {stock.Count}");
            for (int k = 0; k < Math.Min(made.Count, stock.Count); k++)
            {
                particles++;
                if (!Equal(made[k], State(stock[k])))
                    wrong.Add($"{e[0]} {effects} particle {k}: {Diff(made[k], stock[k])}");
            }
            wrong.AddRange(rec.Wrong.Take(3));
            if (rec.Remaining != 0)
                wrong.Add($"{e[0]} {effects}: {rec.Remaining} draw(s) stock made and the port did not");
            if (wrong.Count > 20)
                break;
        }

        Assert.True(blood == 4 && lines == 2 && particles == 705, $"the replay reached {blood} stream(s), {lines} line(s), {particles} particle(s)");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
    }

    [Fact]
    public void EveryStepIsStocksStep()
    {
        var wrong = new List<string>();
        float dt = 0, gravity = 0;
        string[]? input = null;
        int steps = 0, deaths = 0;
        var rec = new Recorded(Rows.Where(r => r[0] == "tpr" && r[1] == "i"));
        foreach (var r in Rows)
        {
            switch (r[0])
            {
                case "tpw":
                    dt = F(r[2]);
                    gravity = F(r[3]);
                    break;
                case "tpi":
                    input = r;
                    break;
                case "tpo":
                    TeParticle p = State(input!);
                    bool alive = TeParticles.Step(ref p, dt, gravity, rec);
                    steps++;
                    if (r[3] == "removed")
                    {
                        deaths++;
                        if (alive)
                            wrong.Add($"step {steps}: lived, stock removed it ({string.Join(' ', input!)})");
                    }
                    else if (!alive || !Equal(p, State(r)))
                    {
                        wrong.Add($"step {steps}: {(alive ? "" : "died, ")}{Diff(p, r)}");
                    }
                    break;
            }
            if (wrong.Count > 20)
                break;
        }

        Assert.True(steps > 5000 && deaths == 640, $"the replay reached {steps} step(s), {deaths} death(s)");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void EveryDrawIsStocksDraw()
    {
        var wrong = new List<string>();
        ParticleView view = default;
        int drawn = 0;
        foreach (var r in Rows)
        {
            if (r[0] == "tpv")
            {
                view = new ParticleView(Enumerable.Range(2, 12).Select(k => F(r[k])).ToArray(), 0);
                Assert.Equal(TeParticles.ParticleSize, F(r[14]));
                continue;
            }
            if (r[0] != "tpd")
                continue;
            drawn++;
            var p = new TeParticle { Position = V(r, 2), R = (byte)I(r[5]), G = (byte)I(r[6]), B = (byte)I(r[7]), A = (byte)I(r[8]) };
            ParticleQuadRaw q = TeParticles.Quad(p);
            Vector viewPos = view.TransformPoint(q.Position);
            if (!Same(viewPos, V(r, 10)) || !Same(q.Color, V(r, 13)) || !Same(q.Alpha, F(r[16]))
                || q.Kind != (int)ParticleQuadKind.ColorSize || q.FixedSort != 0)
                wrong.Add($"draw {drawn}: at {viewPos.X:G9} {viewPos.Y:G9} {viewPos.Z:G9} colour {q.Color.X:G9} {q.Color.Y:G9} {q.Color.Z:G9} " +
                          $"alpha {q.Alpha:G9}; stock {string.Join(' ', r)}");
            if (wrong.Count > 20)
                break;
        }

        Assert.True(drawn > 1000, $"the replay reached {drawn} draw(s)");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }
}
