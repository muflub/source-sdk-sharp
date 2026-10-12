using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The large funnel against stock (lane c5-particles):
/// <c>C_TELargeFunnel::CreateFunnel</c>'s 484 sprites, forward and reversed, as
/// <c>Fixtures/funnel-stock.tsv.gz</c> recorded them (README beside it). Bit for
/// bit, with the recorded draws fed back.
/// </summary>
public class FunnelOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static Vector V(string[] r, int at) => new(F(r[at]), F(r[at + 1]), F(r[at + 2]));

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static bool Same(Vector a, Vector b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

    private static string G(Vector v) => $"{v.X:G9} {v.Y:G9} {v.Z:G9}";

    private static List<List<string[]>> Units()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "funnel-stock.tsv.gz");
        Assert.True(File.Exists(path), path + " is missing; a replay of nothing would pass");
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        var units = new List<List<string[]>>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] r = line.Split('\t');
            if (r[0] == "lfi")
                units.Add([r]);
            else
                units[^1].Add(r);
        }
        return units;
    }

    private sealed class Recorded(IEnumerable<string[]> unit) : IParticleRandom, IParticleViewSource, IParticlePixelVisibility
    {
        private readonly Queue<string[]> _draws = new(unit.Where(r => r[0] == "shr"));
        public readonly List<string> Wrong = [];

        public bool Drained => _draws.Count == 0;

        public float RandomFloat(float min, float max)
        {
            if (!_draws.TryDequeue(out var r) || r[1] != "f" || !Same(min, F(r[2])) || !Same(max, F(r[3])))
            {
                Wrong.Add($"drew float [{min}, {max}], stock {(r is null ? "none" : string.Join(' ', r))}");
                return min;
            }
            return F(r[4]);
        }

        public int RandomInt(int min, int max)
        {
            if (!_draws.TryDequeue(out var r) || r[1] != "i" || min != I(r[2]) || max != I(r[3]))
            {
                Wrong.Add($"drew int [{min}, {max}], stock {(r is null ? "none" : string.Join(' ', r))}");
                return min;
            }
            return I(r[4]);
        }

        public ParticleView Read() => throw new InvalidOperationException();

        public float FractionVisible(Vector origin, ref int queryHandle) => throw new InvalidOperationException();
    }

    [Fact]
    public void Every_funnel_is_stocks_grid_both_ways()
    {
        var wrong = new List<string>();
        int funnels = 0, particles = 0, forward = 0;
        foreach (var unit in Units())
        {
            string[] e = unit[0];
            string what = $"funnel {++funnels}";
            var rec = new Recorded(unit);
            var effects = new ClientParticleEffects(new RecordingParticleExecutor(), new ParticleEffectKinds());
            var funnel = new LargeFunnel(effects, new ParticleViews(rec), rec, new FxTestRig(), new RandomSeeds(1));
            bool reversed = I(e[4]) != 0;
            forward += reversed ? 0 : 1;
            SimpleSheet sheet = funnel.Create(V(e, 1), reversed, rec);
            Assert.Equal("sprites/flare6", sheet.Material);
            var stock = unit.Where(r => r[0] == "lfp").ToList();
            if (sheet.Pending.Count != stock.Count)
                wrong.Add($"{what}: {sheet.Pending.Count} particles, stock {stock.Count}");
            for (int k = 0; k < Math.Min(stock.Count, sheet.Pending.Count); k++)
            {
                SimpleParticle p = sheet.Pending[k];
                string[] s = stock[k];
                bool ok = Same(p.Position, V(s, 3)) && Same(p.Velocity, V(s, 6)) && Same(p.Lifetime, F(s[9])) && Same(p.DieTime, F(s[10]))
                          && p.R == I(s[11]) && p.G == I(s[12]) && p.B == I(s[13]) && p.StartAlpha == I(s[14]) && p.EndAlpha == I(s[15])
                          && p.StartSize == I(s[16]) && p.EndSize == I(s[17]) && Same(p.Roll, F(s[18])) && Same(p.RollDelta, F(s[19]));
                if (!ok)
                    wrong.Add($"{what} particle {k}: {G(p.Position)} {G(p.Velocity)} {p.DieTime:G9} {p.R},{p.G},{p.B} {p.StartAlpha}/{p.EndAlpha} " +
                              $"{p.StartSize}->{p.EndSize} {p.Roll:G9} against {string.Join(' ', s)}");
                particles++;
            }
            wrong.AddRange(rec.Wrong.Select(w => $"{what}: {w}"));
            if (!rec.Drained)
                wrong.Add($"{what}: stock drew more");
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(funnels == 4 && forward == 1 && particles == 4 * 484, $"{funnels} funnels ({forward} forward), {particles} particles");
    }
}
