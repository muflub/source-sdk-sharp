using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// FX_Dust against stock (lane c5-particles): the clamps, the scatter and every
/// particle's numbers, as <c>Fixtures/dustpuff-stock.tsv.gz</c> recorded them
/// (README beside it). Bit for bit, with the recorded draws and light fed back.
/// </summary>
public class DustPuffOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static Vector V(string[] r, int at) => new(F(r[at]), F(r[at + 1]), F(r[at + 2]));

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static bool Same(Vector a, Vector b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

    private static readonly Lazy<List<string[]>> s_rows = new(() =>
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "dustpuff-stock.tsv.gz");
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

    /// <summary>
    /// Stock's draws in order: <c>c</c> is a <c>rand()</c> (the port's
    /// <c>RandomInt(0, VALVE_RAND_MAX)</c>), <c>f</c>/<c>i</c> the uniform stream
    /// with its range checked.
    /// </summary>
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
            if (!_q.TryDequeue(out var r))
            {
                Wrong.Add($"drew int [{min}, {max}], stock none");
                return min;
            }
            if (min == 0 && max == ParticleRandomExtensions.ValveRandMax)
            {
                if (r[1] != "c")
                    Wrong.Add($"drew rand(), stock {string.Join(' ', r)}");
                return I(r[2]);
            }
            if (r[1] != "i" || min != I(r[2]) || max != I(r[3]))
                Wrong.Add($"drew int [{min}, {max}], stock {string.Join(' ', r)}");
            return I(r[^1]);
        }
    }

    [Fact]
    public void EveryPuffIsStocksPuff()
    {
        var rows = s_rows.Value;
        var wrong = new List<string>();
        int puffs = 0, particles = 0;
        for (int at = 0; at < rows.Count; at++)
        {
            if (rows[at][0] != "dpe")
                continue;
            string[] e = rows[at];
            int end = rows.FindIndex(at + 1, r => r[0] == "dpe");
            if (end < 0)
                end = rows.Count;
            var unit = rows.GetRange(at + 1, end - at - 1);
            puffs++;

            Vector origin = V(e, 1), dir = V(e, 4);
            float size = F(e[7]), speed = F(e[8]);
            var (n, s) = DustPuff.Clamp(size, speed);
            if (n != I(e[10]) || !Same(s, F(e[11])))
                wrong.Add($"puff {puffs}: {n} puff(s) at {s:G9}, stock {e[10]} at {e[11]}");

            var rec = new Recorded(unit.Where(r => r[0] == "dpr"));
            var made = DustPuff.Make(origin, dir, size, speed, V(e, 12), rec);
            var stock = unit.Where(r => r[0] == "dpp").ToList();
            if (made.Count != stock.Count)
                wrong.Add($"puff {puffs}: {made.Count} particle(s), stock {stock.Count}");
            for (int k = 0; k < Math.Min(made.Count, stock.Count); k++)
            {
                SimpleParticle p = made[k];
                string[] r = stock[k];
                particles++;
                bool ok = Same(p.Position, V(r, 1)) && Same(p.DieTime, F(r[4])) && Same(p.Velocity, V(r, 5))
                          && p.R == I(r[8]) && p.G == I(r[9]) && p.B == I(r[10])
                          && p.StartAlpha == I(r[11]) && p.EndAlpha == I(r[12]) && p.StartSize == I(r[13]) && p.EndSize == I(r[14])
                          && Same(p.Roll, F(r[15])) && Same(p.RollDelta, F(r[16])) && p.Material == I(r[17]);
                if (!ok)
                    wrong.Add($"puff {puffs} particle {k}: pos {p.Position.X:G9} {p.Position.Y:G9} {p.Position.Z:G9} die {p.DieTime:G9} " +
                              $"v {p.Velocity.X:G9} {p.Velocity.Y:G9} {p.Velocity.Z:G9} rgb {p.R} {p.G} {p.B} a {p.StartAlpha} " +
                              $"size {p.StartSize}..{p.EndSize} roll {p.Roll} {p.RollDelta:G9} mat {p.Material}; stock {string.Join(' ', r)}");
            }
            wrong.AddRange(rec.Wrong.Take(3).Select(w => $"puff {puffs}: {w}"));
            if (rec.Remaining != 0)
                wrong.Add($"puff {puffs}: {rec.Remaining} draw(s) stock made and the port did not");
        }

        Assert.True(puffs == 11 && particles == 131, $"the replay reached {puffs} puff(s), {particles} particle(s)");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
    }

    [Fact]
    public void TheLightIsSampledWhereStockSampledIt()
    {
        Assert.Equal(new Vector(10, 0, 4), DustPuff.LightSample(new Vector(0, 0, 4), new Vector(1, 0, 0), 10));
    }
}
