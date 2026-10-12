using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The recorded trail fixtures' shared reading (tools/probes/c2sims-trail-probe.diff):
/// frames headed by an <c>F</c> row, the trail's numbers in <c>P</c>, every
/// draw (<c>RF</c>/<c>RI</c>) in the order stock took it, and particle rows
/// keyed by tag: <c>N*</c> spawned, <c>Q*</c> after a simulate step, <c>D*</c>
/// a quad's numbers. A replay random hands the draws back and fails on any the
/// port takes that stock did not.
/// </summary>
internal static class TrailOracle
{
    public static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    public static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    public static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

    public static Vector V(string[] c, int at) => new(F(c[at]), F(c[at + 1]), F(c[at + 2]));

    public sealed class Frame
    {
        public required string[] Head;
        public string[] Params = [];
        public readonly List<string[]> Draws = new();
        public readonly Dictionary<string, List<string[]>> Rows = new();

        public List<string[]> Of(string tag) => Rows.TryGetValue(tag, out var l) ? l : [];

        public float Dt => F(Head[1]);

        public float CurTime => F(Head[2]);

        public Vector Origin => V(Head, 3);

        public Vector Angles => V(Head, 6);

        public Vector Velocity => V(Head, 9);
    }

    public static List<Frame> Load(string name)
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", name);
        Assert.True(File.Exists(path), "the recorded stock trail is missing: " + path);

        var frames = new List<Frame>();
        Frame? cur = null;
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] c = line.Split('\t');
            if (c[0] == "F")
            {
                cur = new Frame { Head = c };
                frames.Add(cur);
                continue;
            }

            Assert.True(cur is not null, $"a '{c[0]}' row before the first frame of {name}");
            if (c[0] == "P")
                cur!.Params = c;
            else if (c[0] is "RF" or "RI")
                cur!.Draws.Add(c);
            else if (c[0][0] is 'N' or 'Q' or 'D' or 'T')
            {
                if (!cur!.Rows.TryGetValue(c[0], out var l))
                    cur.Rows[c[0]] = l = new();
                l.Add(c);
            }
            else
                Assert.Fail($"an unknown row kind '{c[0]}' in {name}");
        }

        Assert.True(frames.Count >= 30, $"{name} parsed ({frames.Count} frames)");
        return frames;
    }

    public sealed class Replay : IParticleRandom
    {
        public readonly Queue<string[]> Pending = new();

        public void Queue(Frame f)
        {
            foreach (var d in f.Draws)
                Pending.Enqueue(d);
        }

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

    /// <summary>A particle row's compared columns: positions, velocity, roll, rolldelta, life, die as bits, then the bytes (not the flags).</summary>
    public static string Row(string[] c) => Row(c.Skip(2).Take(10).Select(F), c.Skip(12).Take(7).Select(I));

    public static string Row(SimpleParticle p)
        => Row([p.Position.X, p.Position.Y, p.Position.Z, p.Velocity.X, p.Velocity.Y, p.Velocity.Z, p.Roll, p.RollDelta, p.Lifetime, p.DieTime],
               [p.R, p.G, p.B, p.StartAlpha, p.EndAlpha, p.StartSize, p.EndSize]);

    private static string Row(IEnumerable<float> floats, IEnumerable<int> ints)
        => string.Join(",", floats.Select(v => Bits(v).ToString("x8", CultureInfo.InvariantCulture))
                                  .Concat(ints.Select(i => i.ToString(CultureInfo.InvariantCulture))));

    public static string Quad(string[] d) => Quad(d.Skip(1).Take(9).Select(F));

    public static string Quad(ParticleQuad q) => Quad([q.Position.X, q.Position.Y, q.Position.Z, q.Color.X, q.Color.Y, q.Color.Z, q.Alpha, q.Size, q.Roll]);

    private static string Quad(IEnumerable<float> values)
        => string.Join(",", values.Select(v => Bits(v).ToString("x8", CultureInfo.InvariantCulture)));

    /// <summary>
    /// The flags byte stock never wrote, as the recording found it, put on the
    /// port's freshly spawned particles (newest first in the emitter, spawn
    /// order in the rows): the replay's one seam into stock's uninitialised memory.
    /// </summary>
    public static void InjectFlags(SimpleEmitter emitter, List<string[]> spawned)
    {
        for (int k = 0; k < spawned.Count; k++)
            emitter.SetFlags(emitter.Particles[spawned.Count - 1 - k].Id, (byte)I(spawned[k][19]));
    }

    /// <summary>Spawns: the port's added particles against the frame's N rows of <paramref name="tag"/>, material and row.</summary>
    public static int CheckSpawned(int f, string tag, List<string[]> want, List<SimpleParticle> have)
    {
        Assert.True(have.Count == want.Count, $"frame {f}: stock spawned {want.Count} {tag}, the port {have.Count}");
        for (int k = 0; k < have.Count; k++)
        {
            Assert.True(have[k].Material == I(want[k][1]), $"frame {f} {tag} {k}: material {have[k].Material} vs stock {want[k][1]}");
            Assert.True(Row(have[k]) == Row(want[k]), $"frame {f} {tag} {k}: stock {string.Join(" ", want[k].Skip(2))}, port {Row(have[k])} vs {Row(want[k])}");
        }

        return have.Count;
    }

    /// <summary>After a simulate step: the live particles (life under die) as a set, against the frame's Q rows.</summary>
    public static int CheckState(int f, string tag, List<string[]> want, IEnumerable<SimpleParticle> have)
    {
        var w = want.Where(q => F(q[10]) < F(q[11])).Select(Row).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var h = have.Select(Row).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.True(w.SequenceEqual(h), $"frame {f} {tag}: stock holds {w.Count}, the port {h.Count}; first stock '{w.FirstOrDefault()}', port '{h.FirstOrDefault()}'");
        return w.Count;
    }

    /// <summary>The quads, as a set, against the frame's D rows.</summary>
    public static int CheckQuads(int f, string tag, List<string[]> want, IEnumerable<ParticleQuad> have)
    {
        var w = want.Select(Quad).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var h = have.Select(Quad).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.True(w.SequenceEqual(h), $"frame {f} {tag}: quads differ ({w.Count} vs {h.Count}); first stock '{w.FirstOrDefault()}', port '{h.FirstOrDefault()}'");
        return w.Count;
    }

    /// <summary>
    /// Lines <paramref name="emitter"/>'s particles up in the order stock
    /// stepped them (the frame's <paramref name="state"/> rows), so a step that
    /// draws takes each particle's own draws. A row is matched to the untaken
    /// particle nearest where it was before the step (its position less its new
    /// velocity times <paramref name="dt"/>): the step moves a particle by
    /// exactly that, so the nearest is the one. See <see cref="SimpleEmitter.Reorder"/>.
    /// </summary>
    public static void StockOrder(SimpleEmitter emitter, List<string[]> state, float dt)
    {
        var taken = new HashSet<int>();
        var ids = new List<int>();
        foreach (var q in state)
        {
            var before = V(q, 2) - V(q, 5) * dt;
            int best = -1;
            float bestD = float.MaxValue;
            foreach (var p in emitter.Particles)
            {
                if (taken.Contains(p.Id))
                    continue;
                var d = p.Position - before;
                float dd = d.X * d.X + d.Y * d.Y + d.Z * d.Z;
                if (dd < bestD)
                {
                    bestD = dd;
                    best = p.Id;
                }
            }

            Assert.True(best >= 0 && bestD < 1e-2f, $"a stepped particle (at {q[2]} {q[3]} {q[4]}) the port does not hold (nearest {bestD})");
            taken.Add(best);
            ids.Add(best);
        }

        emitter.Reorder(ids);
    }
}
