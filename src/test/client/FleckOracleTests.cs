using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The impact flecks against stock: shell_impact_effects.cpp's
/// CreateFleckParticles (the sampling), fx_fleck.cpp's CFleckParticles (the
/// merge list, the step, the draw), as <c>Fixtures/fleck-stock.tsv.gz</c>
/// recorded them (README beside it). Bit for bit, with the recorded random
/// draws and world traces fed back.
/// </summary>
public class FleckOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static Vector V(string[] r, int at) => new(F(r[at]), F(r[at + 1]), F(r[at + 2]));

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static bool Same(Vector a, Vector b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

    private static List<string[]> Rows()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "fleck-stock.tsv.gz");
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
    }

    /// <summary>The recorded draws and traces, in order, each checked against what was asked.</summary>
    private sealed class Recorded(IEnumerable<string[]> rows) : IParticleRandom, IParticleWorldTrace
    {
        private readonly Queue<string[]> _draws = new(rows.Where(r => r[0] is "fkr" or "pcr"));
        private readonly Queue<string[]> _traces = new(rows.Where(r => r[0] == "pct"));
        public readonly List<string> Wrong = [];

        public bool Drained => _draws.Count == 0 && _traces.Count == 0;

        public float RandomFloat(float min, float max)
        {
            if (!_draws.TryDequeue(out var r))
            {
                Wrong.Add("a float draw stock did not take");
                return min;
            }
            int at = r[0] == "fkr" ? 2 : 1;
            if ((r[0] == "fkr" && r[1] != "f") || !Same(min, F(r[at])) || !Same(max, F(r[at + 1])))
                Wrong.Add($"drew float [{min}, {max}], stock {string.Join(' ', r)}");
            return F(r[at + 2]);
        }

        public int RandomInt(int min, int max)
        {
            if (!_draws.TryDequeue(out var r) || r[0] != "fkr" || r[1] != "i")
            {
                Wrong.Add($"drew int [{min}, {max}], stock {(r is null ? "none" : string.Join(' ', r))}");
                return min;
            }
            if (min != I(r[2]) || max != I(r[3]))
                Wrong.Add($"drew int [{min}, {max}], stock [{r[2]}, {r[3]}]");
            return I(r[4]);
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

    private static FleckSystems Systems(IParticleWorldTrace world)
        => new(new ClientParticleEffects(new RecordingParticleExecutor(), new ParticleEffectKinds()), world, new RandomSeeds(1));

    [Fact]
    public void Every_impact_samples_its_flecks_as_stock()
    {
        var rows = Rows();
        int impacts = 0, flecks = 0;
        var wrong = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i][0] != "fki")
                continue;
            int j = i + 1;
            while (j < rows.Count && rows[j][0] is "fkc" or "fke" or "pcs" or "pct" or "pcp" or "fkr" or "fkp")
                j++;
            var unit = rows.GetRange(i, j - i);
            string[] r = rows[i];
            var recorded = new Recorded(unit);
            float[] block = r.Skip(13).Select(F).ToArray();
            var systems = Systems(recorded);
            int n = systems.ImpactFlecks(V(r, 1), V(r, 4), V(r, 7), I(r[10]), F(r[11]), block, recorded);
            impacts++;

            var stock = unit.Where(x => x[0] == "fkp").ToList();
            if (n != stock.Count)
                wrong.Add($"impact {impacts}: {n} flecks, stock {stock.Count}");

            var inOrder = systems.Live.Count > 0 ? SamplingOrder(systems.Live[0]) : [];
            for (int k = 0; k < Math.Min(stock.Count, inOrder.Count); k++)
            {
                var p = inOrder[k];
                string[] s = stock[k];
                if (p.Sheet != I(s[2]) || !Same(p.DieTime, F(s[3])) || p.Size != I(s[4]) || !Same(p.Velocity, V(s, 5))
                    || !Same(p.Roll, F(s[8])) || !Same(p.RollDelta, F(s[9])) || p.R != I(s[10]) || p.G != I(s[11]) || p.B != I(s[12])
                    || !Same(p.Position, V(s, 13)))
                    wrong.Add($"impact {impacts} fleck {k}: {p.Sheet} {p.DieTime} {p.Size} {p.Velocity} {p.Roll} {p.RollDelta} {p.R},{p.G},{p.B} {p.Position} against {string.Join(' ', s)}");
            }
            flecks += stock.Count;
            if (!recorded.Drained)
                wrong.Add($"impact {impacts}: stock took draws or traces the port did not");
            wrong.AddRange(recorded.Wrong.Select(w => $"impact {impacts}: {w}"));
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(impacts >= 50 && flecks >= 500, $"{impacts} impacts, {flecks} flecks");
    }

    // The order the sampler made them, across the two sheets.
    private static List<FleckParticle> SamplingOrder(FleckSystem system)
        => system.Sheets.SelectMany(sh => sh.Pending).OrderBy(p => p.Sequence).ToList();

    [Fact]
    public void Each_creation_merges_into_the_system_stock_merged_into_with_its_box()
    {
        var bySystem = new Dictionary<string, FleckSystem>();
        int creates = 0, merges = 0;
        var wrong = new List<string>();
        foreach (var r in Rows().Where(r => r[0] == "fkc"))
        {
            creates++;
            Vector center = V(r, 2), extents = V(r, 5);
            if (r[10] == "0")
            {
                bySystem[r[9]] = new FleckSystem(r[1], center - extents, center + extents, "a", "b");
                continue;
            }
            merges++;
            var s = bySystem[r[9]];
            if (!s.TryMerge(r[1], center, extents))
                wrong.Add($"create {creates}: stock merged, the port refused");
            if (!Same(s.Mins, V(r, 11)) || !Same(s.Maxs, V(r, 14)))
                wrong.Add($"create {creates}: box ({s.Mins}, {s.Maxs}), stock ({V(r, 11)}, {V(r, 14)})");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(merges >= 20, $"{merges} merges in {creates} creations");
    }

    [Fact]
    public void Every_recorded_step_and_draw_is_reproduced()
    {
        var rows = Rows();
        var collisionOf = new Dictionary<string, string>();       // emitter -> its collision
        var collisions = new Dictionary<string, ParticleCollision>();
        var colours = new Dictionary<string, (byte R, byte G, byte B, byte Size)>();
        var last = new Dictionary<string, FleckParticle>();
        int steps = 0, removed = 0, stopped = 0, draws = 0, uninitialised = 0;
        var wrong = new List<string>();

        for (int i = 0; i < rows.Count; i++)
        {
            string[] r = rows[i];
            switch (r[0])
            {
                case "fke":
                    collisionOf[r[1]] = r[2];
                    break;
                case "pcs":
                {
                    int j = i + 1;
                    while (rows[j][0] != "pcp")
                        j++;
                    var c = new ParticleCollision();
                    var rec = new Recorded(rows.GetRange(i, j - i + 1));
                    c.Setup(V(r, 2), r[5] == "1" ? V(r, 6) : null, F(r[9]), F(r[10]), F(r[11]), F(r[12]), F(r[13]), rec);
                    collisions[r[1]] = c;
                    i = j;
                    break;
                }
                case "fkp":
                    colours[r[1]] = ((byte)I(r[10]), (byte)I(r[11]), (byte)I(r[12]), (byte)I(r[4]));
                    break;
                case "fks":
                {
                    int j = i + 1;
                    while (rows[j][0] != "fkso")
                        j++;
                    var unit = rows.GetRange(i, j - i + 1);
                    string[] o = rows[j];
                    i = j;
                    if (!collisionOf.TryGetValue(r[1], out var cptr) || !collisions.TryGetValue(cptr, out var collision))
                        continue;   // an explosion's flecks: its collision is not this fixture's
                    steps++;
                    var p = new FleckParticle
                    {
                        Position = V(r, 4), Velocity = V(r, 7), Roll = F(r[10]), RollDelta = F(r[11]),
                        Lifetime = F(r[12]), DieTime = F(r[13]),
                    };
                    var pcm = unit.FirstOrDefault(x => x[0] == "pcm");
                    if (pcm is not null)
                    {
                        collision.Gravity = F(pcm[11]);
                        collision.CollisionDampen = F(pcm[12]);
                    }
                    var rec = new Recorded(unit);
                    bool alive = FleckRules.Step(ref p, F(r[3]), collision, rec, rec);
                    if (o[2] == "removed")
                    {
                        removed++;
                        if (alive)
                            wrong.Add($"step {steps}: alive, stock removed it");
                        continue;
                    }
                    bool stockSolid = o[11] == "1";
                    if (stockSolid && collision.ActivePlanes == 0)
                    {
                        // Stock read an uninitialised trace_t: not reproducible, and not wanted.
                        uninitialised++;
                        continue;
                    }
                    stopped += stockSolid ? 1 : 0;
                    if (!alive || !Same(p.Position, V(o, 2)) || !Same(p.Velocity, V(o, 5)) || !Same(p.Roll, F(o[8]))
                        || !Same(p.RollDelta, F(o[9])) || !Same(p.Lifetime, F(o[10])))
                        wrong.Add($"step {steps}: ({p.Position}, {p.Velocity}, {p.Roll}, {p.RollDelta}, {p.Lifetime}) against {string.Join(' ', o)}");
                    if (!rec.Drained)
                        wrong.Add($"step {steps}: stock traced or drew more");
                    wrong.AddRange(rec.Wrong.Select(w => $"step {steps}: {w}"));
                    last[r[2]] = p;
                    break;
                }
                case "fkd":
                {
                    if (!last.TryGetValue(r[2], out var p) || !colours.TryGetValue(r[2], out var c))
                        continue;
                    p.R = c.R; p.G = c.G; p.B = c.B; p.Size = c.Size;
                    draws++;
                    ParticleQuad q = FleckRules.Draw(p);
                    if (!Same(q.Position, V(r, 3)) || !Same(q.Color, V(r, 6)) || !Same(q.Alpha, F(r[9])) || !Same(q.Size, F(r[10])) || !Same(q.Roll, F(r[11])))
                        wrong.Add($"draw {draws}: {q.Position} {q.Color} {q.Alpha} {q.Size} {q.Roll} against {string.Join(' ', r)}");
                    break;
                }
            }
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(steps >= 3000 && removed >= 300 && draws >= 2000,
            $"{steps} steps ({removed} deaths, {stopped} stopped in solid, {uninitialised} stock reads of an uninitialised trace), {draws} draws");
    }
}
