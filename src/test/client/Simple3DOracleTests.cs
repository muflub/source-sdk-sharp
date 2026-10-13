using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// particle_simple3d.cpp against stock (lane c5-particles): CSimple3DEmitter's
/// step (the spin, the collision, the settling) and its draw (the sort, the
/// facing colour, the fade, the quad's edges), as
/// <c>Fixtures/simple3d-stock.tsv.gz</c> recorded them (README beside it).
/// Bit for bit, with the recorded traces and draws fed back.
/// </summary>
public class Simple3DOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static Vector V(string[] r, int at) => new(F(r[at]), F(r[at + 1]), F(r[at + 2]));

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static bool Same(Vector a, Vector b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

    // Stock ran under the engine's flush-to-zero (MXCSR FTZ/DAZ), the test
    // process does not: a denormal the port leaves (a dampened component of
    // 1e-38) is the signed zero stock flushed it to.
    private static float Ftz(float f) => float.IsSubnormal(f) ? (f < 0 ? -0.0f : 0.0f) : f;

    private static bool SameFtz(float a, float b) => Same(Ftz(a), b);

    private static bool SameFtz(Vector a, Vector b) => SameFtz(a.X, b.X) && SameFtz(a.Y, b.Y) && SameFtz(a.Z, b.Z);

    private static string G(Vector v) => $"{v.X:G9} {v.Y:G9} {v.Z:G9}";

    private static readonly Lazy<List<string[]>> s_rows = new(() =>
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "simple3d-stock.tsv.gz");
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

    private sealed class Recorded(IEnumerable<string[]> rows) : IParticleRandom, IParticleWorldTrace
    {
        private readonly Queue<string[]> _draws = new(rows.Where(r => r[0] == "pcr"));
        private readonly Queue<string[]> _traces = new(rows.Where(r => r[0] == "pct"));
        public readonly List<string> Wrong = [];

        public bool Drained => _draws.Count == 0 && _traces.Count == 0;

        public float RandomFloat(float min, float max)
        {
            if (!_draws.TryDequeue(out var r))
            {
                Wrong.Add("a draw stock did not take");
                return min;
            }
            if (!Same(min, F(r[1])) || !Same(max, F(r[2])))
                Wrong.Add($"drew [{min}, {max}], stock {string.Join(' ', r)}");
            return F(r[3]);
        }

        public int RandomInt(int min, int max)
        {
            Wrong.Add("RandomInt");
            return min;
        }

        public ParticleTrace TraceLine(Vector start, Vector end)
        {
            if (!_traces.TryDequeue(out var r))
            {
                Wrong.Add("a trace stock did not make");
                return new ParticleTrace { Fraction = 1 };
            }
            if (!Same(start, V(r, 1)) || !Same(end, V(r, 4)))
                Wrong.Add($"traced {start} -> {end}, stock {V(r, 1)} -> {V(r, 4)}");
            return new ParticleTrace { Fraction = F(r[8]), EndPos = V(r, 9), Normal = V(r, 12), Dist = F(r[15]), AllSolid = r[16] == "1", StartSolid = r[17] == "1" };
        }
    }

    [Fact]
    public void Every_recorded_step_is_reproduced()
    {
        var rows = s_rows.Value;
        var collisions = new Dictionary<string, ParticleCollision>();
        int steps = 0, removed = 0, settled = 0, traced = 0, unknown = 0;
        var wrong = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            string[] r = rows[i];
            if (r[0] == "pcs")
            {
                int j = i + 1;
                while (rows[j][0] != "pcp")
                    j++;
                var rec = new Recorded(rows.GetRange(i, j - i + 1));
                var c = new ParticleCollision();
                c.Setup(V(r, 2), r[5] == "1" ? V(r, 6) : null, F(r[9]), F(r[10]), F(r[11]), F(r[12]), F(r[13]), rec);
                collisions[r[1]] = c;
                i = j;
                continue;
            }
            if (r[0] != "s3s")
                continue;
            int k = i + 1;
            while (rows[k][0] != "s3so")
                k++;
            var unit = rows.GetRange(i, k - i + 1);
            string[] o = rows[k];
            i = k;

            var pcm = unit.FirstOrDefault(x => x[0] == "pcm");
            var p = new Particle3D
            {
                Position = V(r, 3 + 1), Velocity = V(r, 7), Angles = V(r, 10), AngSpeed = F(r[13]), LifeRemaining = F(r[14]),
            };
            ParticleCollision collision;
            if (pcm is null)
            {
                collision = new ParticleCollision();
            }
            else if (!collisions.TryGetValue(pcm[1], out var known))
            {
                unknown++;
                continue;
            }
            else
            {
                collision = known;
                collision.Gravity = F(pcm[11]);
                collision.CollisionDampen = F(pcm[12]);
            }
            steps++;
            traced += unit.Any(x => x[0] == "pct") ? 1 : 0;
            var rec2 = new Recorded(unit);
            bool alive = Simple3DRules.Step(ref p, F(r[3]), collision, rec2, rec2);
            if (o[2] == "removed")
            {
                removed++;
                if (alive)
                    wrong.Add($"step {steps}: alive, stock removed it");
            }
            else
            {
                settled += o[13] != "1" ? 1 : 0;
                if (!alive || !SameFtz(p.Position, V(o, 2)) || !SameFtz(p.Velocity, V(o, 5)) || !SameFtz(p.Angles, V(o, 8))
                    || !SameFtz(p.AngSpeed, F(o[11])) || !Same(p.LifeRemaining, F(o[12])))
                    wrong.Add($"step {steps}: ({G(p.Position)} {G(p.Velocity)} {G(p.Angles)} {p.AngSpeed:G9} {p.LifeRemaining:G9}) against {string.Join(' ', o)}");
            }
            if (!rec2.Drained)
                wrong.Add($"step {steps}: stock traced or drew more");
            wrong.AddRange(rec2.Wrong.Select(w => $"step {steps}: {w}"));
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(steps >= 1500 && removed >= 150 && settled >= 500 && traced >= 300,
            $"{steps} steps ({removed} deaths, {settled} struck, {traced} traced, {unknown} of an unknown setup)");
    }

    [LinuxOnlyFact("a golden recorded bit for bit against glibc's libm; the Windows C runtime rounds the last bit of sin, cos and pow differently")]
    public void Every_recorded_draw_is_reproduced_in_its_view()
    {
        int draws = 0, back = 0, fading = 0;
        var wrong = new List<string>();
        var m = new float[12];
        foreach (var r in s_rows.Value.Where(x => x[0] == "s3d"))
        {
            var view = new ParticleView(m, 0, V(r, 2), V(r, 5));
            var p = new Particle3D
            {
                Position = V(r, 8), Angles = V(r, 11), LifeRemaining = F(r[14]),
                FrontR = (byte)I(r[15]), FrontG = (byte)I(r[16]), FrontB = (byte)I(r[17]),
                BackR = (byte)I(r[18]), BackG = (byte)I(r[19]), BackB = (byte)I(r[20]), Size = (byte)I(r[21]),
            };
            ParticleQuad q = Simple3DRules.Draw(p, view);
            draws++;
            Vector front = new(p.FrontR / 255.0f, p.FrontG / 255.0f, p.FrontB / 255.0f);
            back += Same(V(r, 24), front) ? 0 : 1;
            fading += F(r[27]) < 1 ? 1 : 0;
            if (!Same(q.SortKey, F(r[23])) || !Same(q.Color, V(r, 24)) || !Same(q.Alpha, F(r[27])) || !Same(q.Normal, V(r, 28))
                || !Same(q.Corner2, V(r, 31) * p.Size) || !Same(q.Corner3, V(r, 34) * p.Size) || !Same(q.Position, p.Position)
                || q.Kind != ParticleQuadKind.Angles3D)
                wrong.Add($"draw {draws}: {q.SortKey:G9} {G(q.Color)} {q.Alpha:G9} {G(q.Normal)} {G(q.Corner2)} {G(q.Corner3)} against {string.Join(' ', r)}");
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(draws >= 1000 && back >= 100 && fading >= 100, $"{draws} draws ({back} showing the back, {fading} fading)");
    }
}
