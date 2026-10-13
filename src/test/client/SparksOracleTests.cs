using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// fx_sparks.cpp against stock (lane c5-particles): FX_ElectricSpark,
/// FX_MetalSpark and FX_EnergySplash's emitters and particles, CTrailParticles'
/// step and draw, and CSimpleGlowEmitter's visibility gate, as
/// <c>Fixtures/sparks-stock.tsv.gz</c> recorded them (README beside it). Bit
/// for bit, with the recorded random draws, traces and visibility answers fed
/// back.
/// </summary>
public class SparksOracleTests
{
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static Vector V(string[] r, int at) => new(F(r[at]), F(r[at + 1]), F(r[at + 2]));

    // Stock ran under the engine's flush-to-zero (MXCSR FTZ/DAZ), the test
    // process does not: a denormal the port leaves (a dampened velocity of
    // 1e-38) is the signed zero stock flushed it to. Twice in the fixture.
    private static float Ftz(float f) => float.IsSubnormal(f) ? (f < 0 ? -0.0f : 0.0f) : f;

    private static bool SameFtz(Vector a, Vector b)
        => Same(Ftz(a.X), b.X) && Same(Ftz(a.Y), b.Y) && Same(Ftz(a.Z), b.Z);

    private static string G(Vector v) => $"{v.X:G9} {v.Y:G9} {v.Z:G9}";

    private static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static bool Same(Vector a, Vector b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);

    private static readonly Lazy<List<string[]>> s_rows = new(() =>
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "sparks-stock.tsv.gz");
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

    private static List<string[]> Rows() => s_rows.Value;

    private static List<string[]> GaussRows()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "gauss-stock.tsv.gz");
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
        private readonly Queue<string[]> _draws = new(rows.Where(r => r[0] is "skr" or "gxr" or "pcr"));
        private readonly Queue<string[]> _traces = new(rows.Where(r => r[0] == "pct"));
        public readonly List<string> Wrong = [];

        public bool Drained => _draws.Count == 0 && _traces.Count == 0;

        public float RandomFloat(float min, float max)
        {
            if (!_draws.TryDequeue(out var r))
            {
                Wrong.Add($"a float draw [{min}, {max}] stock did not take");
                return min;
            }
            bool tagged = r[0] is "skr" or "gxr";
            int at = tagged ? 2 : 1;
            if ((tagged && r[1] != "f") || !Same(min, F(r[at])) || !Same(max, F(r[at + 1])))
                Wrong.Add($"drew float [{min}, {max}], stock {string.Join(' ', r)}");
            return F(r[at + 2]);
        }

        public int RandomInt(int min, int max)
        {
            if (!_draws.TryDequeue(out var r) || r[0] is not ("skr" or "gxr") || r[1] != "i")
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

    private sealed class Quads : IFxQuadSink
    {
        public readonly List<(Vector Origin, Vector Normal, float StartSize, float Yaw, string Material)> Added = [];

        public void AddQuad(Vector origin, Vector normal, float startSize, float endSize, float sizeBias, float startAlpha,
                            float endAlpha, float alphaBias, float yaw, float deltaYaw, Vector color, float lifeTime,
                            string material, FxQuadFlags flags)
            => Added.Add((origin, normal, startSize, yaw, material));
    }

    private sealed class Environment : ISparkEnvironment
    {
        public float CurTime { get; set; }
        public float ThrottleScaling { get; set; } = 1;
        public bool DrawMetalSpark { get; set; } = true;
    }

    private sealed class NoView : IParticleViewSource, IParticlePixelVisibility
    {
        public ParticleView Read() => throw new InvalidOperationException("a spawn reads no view");

        public float FractionVisible(Vector origin, ref int queryHandle) => throw new InvalidOperationException("a spawn tests nothing");
    }

    private static (SparkEffects Sparks, Quads Quads, Environment Env) Rig(IParticleWorldTrace world)
    {
        var quads = new Quads();
        var env = new Environment();
        var none = new NoView();
        var sparks = new SparkEffects(new ClientParticleEffects(new RecordingParticleExecutor(), new ParticleEffectKinds()),
                                      world, new ParticleViews(none), none, quads, env, new RandomSeeds(1));
        return (sparks, quads, env);
    }

    private static readonly HashSet<string> SpawnTags = ["pcs", "pct", "pcp", "pcr", "skc", "skr", "skp", "skq"];

    private static IEnumerable<List<string[]>> SpawnUnits(List<string[]> rows)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i][0] is not ("ske" or "skm" or "skn"))
                continue;
            int j = i + 1;
            while (j < rows.Count && SpawnTags.Contains(rows[j][0]))
                j++;
            yield return rows.GetRange(i, j - i);
        }
    }

    private static string CompareTrail(TrailParticle p, string[] s)
        => Same(p.Position, V(s, 4)) && Same(p.Lifetime, F(s[7])) && Same(p.DieTime, F(s[8])) && Same(p.Width, F(s[9]))
           && Same(p.Length, F(s[10])) && Same(p.Velocity, V(s, 11)) && p.R == I(s[14]) && p.G == I(s[15]) && p.B == I(s[16])
           && p.A == I(s[17])
            ? ""
            : $"({p.Position} {p.Lifetime} {p.DieTime} {p.Width} {p.Length} {p.Velocity} {p.R},{p.G},{p.B},{p.A}) against {string.Join(' ', s)}";

    private static string CompareSimple(SimpleParticle p, string[] s)
        => Same(p.Position, V(s, 4)) && Same(p.Velocity, V(s, 7)) && Same(p.Lifetime, F(s[10])) && Same(p.DieTime, F(s[11]))
           && p.R == I(s[12]) && p.G == I(s[13]) && p.B == I(s[14]) && p.StartAlpha == I(s[15]) && p.EndAlpha == I(s[16])
           && p.StartSize == I(s[17]) && p.EndSize == I(s[18]) && Same(p.Roll, F(s[19])) && Same(p.RollDelta, F(s[20]))
            ? ""
            : $"({p.Position} {p.Velocity} {p.Lifetime} {p.DieTime} {p.R},{p.G},{p.B} {p.StartAlpha}->{p.EndAlpha} {p.StartSize}->{p.EndSize} {p.Roll} {p.RollDelta}) against {string.Join(' ', s)}";

    /// <summary>
    /// <c>FX_SparkFan</c> (the ManhackSparks effect), from
    /// <c>Fixtures/sparkfan-stock.tsv.gz</c>: its collision set-up, traces and
    /// draws, and its thirty-five sparks.
    /// </summary>
    [Fact]
    public void Every_spark_fan_is_stocks()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "sparkfan-stock.tsv.gz");
        Assert.True(File.Exists(path), path + " is missing; a replay of nothing would pass");
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        var rows = new List<string[]>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && line[0] != '#')
                rows.Add(line.Split('\t'));
        }

        int fans = 0, particles = 0;
        var wrong = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i][0] != "skf")
                continue;
            int j = i + 1;
            while (j < rows.Count && rows[j][0] != "skf")
                j++;
            var unit = rows.GetRange(i, j - i);
            string[] f = unit[0];
            var recorded = new Recorded(unit);
            var (sparks, _, _) = Rig(recorded);
            SparkBurst burst = sparks.SparkFan(V(f, 1), V(f, 4), recorded);
            fans++;
            var trail = Assert.IsType<TrailEmitter>(Assert.Single(burst.Emitters));
            Assert.Equal(TrailFlags.VelocityDampen | TrailFlags.Collide, trail.Flags);
            var stock = unit.Where(x => x[0] == "skp").ToList();
            if (stock.Count != trail.Pending.Count)
                wrong.Add($"fan {fans}: {trail.Pending.Count} sparks, stock {stock.Count}");
            for (int k = 0; k < Math.Min(stock.Count, trail.Pending.Count); k++)
            {
                string w = CompareTrail(trail.Pending[k], stock[k]);
                if (w.Length > 0)
                    wrong.Add($"fan {fans} spark {k}: {w}");
                particles++;
            }
            wrong.AddRange(recorded.Wrong.Select(w => $"fan {fans}: {w}"));
            if (!recorded.Drained)
                wrong.Add($"fan {fans}: stock drew or traced more");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(fans == 3 && particles == 105, $"{fans} fans, {particles} sparks");
    }

    [NotOnCiFact("a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit")]
    public void Every_spark_routine_makes_stocks_emitters_and_particles()
    {
        int electric = 0, metal = 0, splash = 0, particles = 0, setups = 0;
        var wrong = new List<string>();
        foreach (var unit in SpawnUnits(Rows()))
        {
            string[] r = unit[0];
            var recorded = new Recorded(unit);
            var (sparks, quads, env) = Rig(recorded);
            var created = unit.Where(x => x[0] == "skc").ToList();
            var glow = created.FirstOrDefault(x => x[2] == "glow");
            if (glow is not null)
                env.CurTime = F(glow[7]);

            SparkBurst burst;
            string what;
            switch (r[0])
            {
                case "ske":
                    electric++;
                    what = $"electric {electric}";
                    burst = sparks.ElectricSpark(V(r, 1), I(r[4]), I(r[5]), r[6] == "1" ? V(r, 7) : null, recorded);
                    break;
                case "skm":
                    metal++;
                    what = $"metal {metal}";
                    if (r[12] != "1")
                        continue;
                    burst = sparks.MetalSpark(V(r, 1), V(r, 4), V(r, 7), I(r[10]), F(r[11]), recorded);
                    break;
                default:
                    splash++;
                    what = $"splash {splash}";
                    burst = sparks.EnergySplash(V(r, 1), V(r, 4), recorded);
                    break;
            }

            // Stock's emitters in creation order; a glow is two sheets here.
            var kinds = created.SelectMany(c => c[2] == "glow" ? new[] { "simple", "simple" } : new[] { c[2] == "trail" ? "trail" : "simple" }).ToList();
            var portKinds = burst.Emitters.Select(e => e is TrailEmitter ? "trail" : "simple").ToList();
            if (!kinds.SequenceEqual(portKinds))
                wrong.Add($"{what}: emitters {string.Join(',', portKinds)}, stock {string.Join(',', kinds)}");

            var trails = burst.Emitters.OfType<TrailEmitter>().ToList();
            var sheets = burst.Emitters.OfType<SimpleSheet>().ToList();
            int t = 0;
            foreach (var c in created)
            {
                var stock = unit.Where(x => x[0] == "skp" && x[1] == c[1]).ToList();
                if (c[2] == "trail")
                {
                    if (t >= trails.Count)
                        break;
                    var port = trails[t++].Pending;
                    if (port.Count != stock.Count)
                        wrong.Add($"{what} {c[3]}: {port.Count} sparks, stock {stock.Count}");
                    for (int k = 0; k < Math.Min(port.Count, stock.Count); k++)
                    {
                        string w = CompareTrail(port[k], stock[k]);
                        if (w.Length > 0)
                            wrong.Add($"{what} {c[3]} spark {k}: {w}");
                    }
                    particles += stock.Count;
                }
                else
                {
                    foreach (var group in stock.GroupBy(x => x[^1]))
                    {
                        var sheet = sheets.FirstOrDefault(s => s.Material == group.Key);
                        var port = sheet?.Pending ?? [];
                        var list = group.ToList();
                        if (port.Count != list.Count)
                            wrong.Add($"{what} {c[3]} {group.Key}: {port.Count} particles, stock {list.Count}");
                        for (int k = 0; k < Math.Min(port.Count, list.Count); k++)
                        {
                            string w = CompareSimple(port[k], list[k]);
                            if (w.Length > 0)
                                wrong.Add($"{what} {c[3]} {group.Key} {k}: {w}");
                        }
                        particles += list.Count;
                    }
                }
            }

            // The big electric sparks' collision, set up as stock's was.
            var pcp = unit.FirstOrDefault(x => x[0] == "pcp");
            if (pcp is not null && trails.Count > 0)
            {
                setups++;
                var c = trails[0].Collision;
                int planes = I(pcp[2]);
                if (c.ActivePlanes != planes || (trails[0].Flags & TrailFlags.Collide) == 0)
                    wrong.Add($"{what}: {c.ActivePlanes} planes (flags {trails[0].Flags}), stock {planes}");
                for (int i = 0; i < Math.Min(planes, c.ActivePlanes); i++)
                {
                    var (n, d) = c.Plane(i);
                    if (!Same(n, V(pcp, 3 + i * 4)) || !Same(d, F(pcp[6 + i * 4])))
                        wrong.Add($"{what} plane {i}: ({n}, {d}) against {string.Join(' ', pcp)}");
                }
            }

            var skq = unit.FirstOrDefault(x => x[0] == "skq");
            if (skq is not null)
            {
                var q = quads.Added.FirstOrDefault();
                if (quads.Added.Count != 1 || !Same(q.Origin, V(skq, 2)) || !Same(q.Normal, V(skq, 5)) || !Same(q.Yaw, F(skq[8]))
                    || !Same(q.StartSize, F(skq[9])) || q.Material != "effects/yellowflare")
                    wrong.Add($"{what}: quad {q} against {string.Join(' ', skq)}");
            }
            if (r[0] == "skn" && quads.Added.Count != 2)
                wrong.Add($"{what}: {quads.Added.Count} quads, stock 2");

            if (!recorded.Drained)
                wrong.Add($"{what}: stock took draws or traces the port did not");
            wrong.AddRange(recorded.Wrong.Select(w => $"{what}: {w}"));
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(electric >= 10 && metal >= 30 && splash >= 20 && setups >= 10 && particles >= 2000,
            $"{electric} electric, {metal} metal, {splash} splashes, {setups} setups, {particles} particles");
    }

    [Fact]
    public void Every_recorded_trail_step_is_reproduced()
    {
        var rows = Rows();
        var collisions = new Dictionary<string, ParticleCollision>();
        int steps = 0, removed = 0, traced = 0, unknown = 0;
        var wrong = new List<string>();

        for (int i = 0; i < rows.Count; i++)
        {
            string[] r = rows[i];
            if (r[0] is "pcs" or "xpcs")
            {
                // An unkept spawn's setup is kept with its tags x-prefixed (README).
                string prefix = r[0] == "xpcs" ? "x" : "";
                int j = i + 1;
                while (rows[j][0] != prefix + "pcp")
                    j++;
                var rec = new Recorded(rows.GetRange(i, j - i + 1).Select(x => x[0].StartsWith('x') ? [x[0][1..], .. x[1..]] : x));
                var c = new ParticleCollision();
                c.Setup(V(r, 2), r[5] == "1" ? V(r, 6) : null, F(r[9]), F(r[10]), F(r[11]), F(r[12]), F(r[13]), rec);
                collisions[r[1]] = c;
                i = j;
                continue;
            }
            if (r[0] != "sts")
                continue;

            int k = i + 1;
            while (rows[k][0] != "stso")
                k++;
            var unit = rows.GetRange(i, k - i + 1);
            string[] o = rows[k];
            i = k;

            var flags = (TrailFlags)I(r[4]);
            string[] pcm = unit.First(x => x[0] == "pcm");
            ParticleCollision collision;
            if ((flags & TrailFlags.Collide) != 0)
            {
                if (!collisions.TryGetValue(pcm[1], out var known))
                {
                    unknown++;   // its Setup was in a spawn the fixture did not keep
                    continue;
                }
                collision = known;
            }
            else
            {
                collision = new ParticleCollision();
            }
            collision.Gravity = F(pcm[11]);
            collision.CollisionDampen = F(pcm[12]);

            steps++;
            traced += unit.Any(x => x[0] == "pct") ? 1 : 0;
            var p = new TrailParticle { Position = V(r, 6), Velocity = V(r, 9), Lifetime = F(r[12]), DieTime = F(r[13]) };
            var rec2 = new Recorded(unit);
            bool alive = TrailRules.Step(ref p, F(r[3]), flags, F(r[5]), collision, rec2, rec2);
            if (o[2] == "removed")
            {
                removed++;
                if (alive)
                    wrong.Add($"step {steps}: alive, stock removed it");
            }
            else if (!alive || !SameFtz(p.Position, V(o, 2)) || !SameFtz(p.Velocity, V(o, 5)) || !Same(p.Lifetime, F(o[8])))
            {
                wrong.Add($"step {steps}: ({G(p.Position)}, {G(p.Velocity)}, {p.Lifetime:G9}) against {string.Join(' ', o)}");
            }
            if (!rec2.Drained)
                wrong.Add($"step {steps}: stock traced or drew more");
            wrong.AddRange(rec2.Wrong.Select(w => $"step {steps}: {w}"));
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(steps >= 1500 && removed >= 200 && traced >= 300,
            $"{steps} steps ({removed} deaths, {traced} traced, {unknown} of an unknown setup)");
    }

    [Fact]
    public void Every_recorded_trail_draw_is_reproduced_in_its_view()
    {
        int draws = 0, clipped = 0;
        var wrong = new List<string>();
        var m = new float[12];
        foreach (var r in Rows().Where(x => x[0] == "std"))
        {
            for (int i = 0; i < 12; i++)
                m[i] = F(r[5 + i]);
            var view = new ParticleView(m, I(r[4]));
            var p = new TrailParticle
            {
                Position = V(r, 17), Velocity = V(r, 20), Lifetime = F(r[23]), DieTime = F(r[24]), Length = F(r[25]),
                Width = F(r[26]), R = (byte)I(r[27]), G = (byte)I(r[28]), B = (byte)I(r[29]), A = (byte)I(r[30]),
            };
            ParticleQuad q = TrailRules.Draw(p, (TrailFlags)I(r[3]), view);
            draws++;
            bool ok = r[43] == "1";
            clipped += ok ? 0 : 1;
            bool same = Same(q.SortKey, F(r[34])) && Same(q.Color, V(r, 39)) && Same(q.Alpha, F(r[42]))
                        && (q.Size > 0) == ok
                        && (!ok || (Same(q.Position, V(r, 44)) && Same(q.Normal, V(r, 47)) && Same(q.Corner2, V(r, 50))
                                    && Same(q.Corner3, V(r, 53))));
            if (!same || q.Kind != ParticleQuadKind.Tracer || !q.FixedSortKey)
                wrong.Add($"draw {draws}: {q.SortKey} {q.Color} {q.Alpha} {q.Size} {q.Position} {q.Normal} {q.Corner2} {q.Corner3} against {string.Join(' ', r)}");
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(draws >= 1500 && clipped >= 200 && draws - clipped >= 1000, $"{draws} draws, {clipped} clipped away");
    }

    private sealed class RecordedVisibility : IParticlePixelVisibility
    {
        public float? Answer;
        public bool Asked;

        public float FractionVisible(Vector origin, ref int queryHandle)
        {
            Asked = true;
            return Answer ?? 0.0f;
        }
    }

    [Fact]
    public void Every_glow_tests_simulates_and_draws_as_stock()
    {
        var gates = new Dictionary<string, GlowGate>();
        var visibility = new RecordedVisibility();
        int renders = 0, tested = 0, hiddenYoung = 0, steps = 0, deaths = 0, waits = 0;
        var wrong = new List<string>();
        foreach (var r in Rows())
        {
            switch (r[0])
            {
                case "skc" or "skx" when r[2] == "glow":
                {
                    float now = F(r[7]);
                    gates[r[1]] = new GlowGate(V(r, 4), (float)(now + 0.2), now);
                    break;
                }
                case "skc" or "skx":
                    gates.Remove(r[1]);   // the address went to another emitter
                    break;
                case "sgr":
                {
                    if (!gates.TryGetValue(r[1], out var gate))
                        continue;
                    renders++;
                    if (!Same(gate.StartTime, F(r[7])) || gate.WasTested != I(r[3]) || gate.IsVisible != I(r[4]))
                    {
                        wrong.Add($"render {renders}: state ({gate.StartTime}, {gate.WasTested}, {gate.IsVisible}) against {string.Join(' ', r)}");
                        break;
                    }
                    float stockVisible = F(r[5]);
                    visibility.Answer = stockVisible;
                    visibility.Asked = false;
                    bool draws = gate.Draws(I(r[2]), F(r[6]), visibility);
                    bool stockAsked = stockVisible >= 0.0f;
                    tested += stockAsked ? 1 : 0;
                    hiddenYoung += stockAsked && stockVisible == 0.0f && r[11] == "0" && I(r[9]) == I(r[3]) ? 1 : 0;
                    if (visibility.Asked != stockAsked || draws != (r[11] == "1") || gate.WasTested != I(r[9]) || gate.IsVisible != I(r[10]))
                        wrong.Add($"render {renders}: asked {visibility.Asked} draws {draws} -> ({gate.WasTested}, {gate.IsVisible}) against {string.Join(' ', r)}");
                    break;
                }
                case "sgs":
                {
                    if (!gates.TryGetValue(r[1], out var gate))
                        continue;
                    steps++;
                    if (!Same(gate.DeathTime, F(r[3])))
                        wrong.Add($"step {steps}: death {gate.DeathTime}, stock {r[3]}");
                    GlowStep step = gate.Step(F(r[2]));
                    string port = step switch { GlowStep.Die => "dead", GlowStep.Wait => "untested", _ => "step" };
                    deaths += port == "dead" ? 1 : 0;
                    waits += port == "untested" ? 1 : 0;
                    if (port != r[4])
                        wrong.Add($"step {steps}: {port} against {string.Join(' ', r)}");
                    break;
                }
            }
            if (wrong.Count > 20)
                break;
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(renders >= 100 && tested >= 20 && hiddenYoung >= 5 && steps >= 100 && deaths >= 5 && waits >= 20,
            $"{renders} renders ({tested} tested, {hiddenYoung} hidden while young), {steps} steps ({deaths} deaths, {waits} waits)");
    }

    [NotOnCiFact("a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit")]
    public void Every_gauss_explosion_makes_stocks_ring_and_electric_spark()
    {
        var rows = GaussRows();
        int explosions = 0, sparks = 0, electric = 0;
        var wrong = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i][0] != "gxe")
                continue;
            int j = i + 1;
            while (j < rows.Count && rows[j][0] != "gxe")
                j++;
            var unit = rows.GetRange(i, j - i);
            string[] r = rows[i];
            explosions++;
            var recorded = new Recorded(unit);
            var (fx, _, env) = Rig(recorded);
            var glow = unit.FirstOrDefault(x => x[0] == "skc" && x[2] == "glow");
            if (glow is not null)
                env.CurTime = F(glow[7]);

            SparkBurst burst = fx.GaussExplosion(V(r, 1), V(r, 4), recorded);
            var trails = burst.Emitters.OfType<TrailEmitter>().ToList();
            var stock = unit.Where(x => x[0] == "gxp").ToList();
            var ring = trails.Count > 0 ? trails[0].Pending : [];
            if (ring.Count != stock.Count)
                wrong.Add($"gauss {explosions}: {ring.Count} sparks, stock {stock.Count}");
            for (int k = 0; k < Math.Min(ring.Count, stock.Count); k++)
            {
                TrailParticle p = ring[k];
                string[] s = stock[k];
                if (!Same(p.Position, V(s, 2)) || !Same(p.Lifetime, F(s[5])) || !Same(p.DieTime, F(s[6])) || !Same(p.Width, F(s[7]))
                    || !Same(p.Length, F(s[8])) || !Same(p.Velocity, V(s, 9)) || p.R != I(s[12]) || p.G != I(s[13]) || p.B != I(s[14]) || p.A != I(s[15]))
                    wrong.Add($"gauss {explosions} spark {k}: ({p.Position} {p.DieTime} {p.Width} {p.Length} {p.Velocity} {p.A}) against {string.Join(' ', s)}");
            }
            sparks += stock.Count;

            // The ring's collision, and the electric spark along the last direction.
            var pcp = unit.First(x => x[0] == "pcp");
            if (trails.Count == 0 || trails[0].Collision.ActivePlanes != I(pcp[2]) || (trails[0].Flags & TrailFlags.Collide) == 0)
                wrong.Add($"gauss {explosions}: the ring's collision differs from {string.Join(' ', pcp)}");
            var ske = unit.First(x => x[0] == "ske");
            var gxs = unit.First(x => x[0] == "gxs");
            if (!Same(V(ske, 7), V(gxs, 1)) || I(ske[4]) != 1 || I(ske[5]) != 1)
                wrong.Add($"gauss {explosions}: the electric spark {string.Join(' ', ske)} is not along {string.Join(' ', gxs)}");
            int stockElectric = unit.Count(x => x[0] == "skp");
            int portElectric = trails.Skip(1).Sum(t => t.Pending.Count) + burst.Emitters.OfType<SimpleSheet>().Sum(s => s.Pending.Count);
            if (portElectric != stockElectric || trails.Count != 3)
                wrong.Add($"gauss {explosions}: electric spark {portElectric} particles in {trails.Count - 1} trails, stock {stockElectric}");
            electric += stockElectric;

            if (!recorded.Drained)
                wrong.Add($"gauss {explosions}: stock took draws or traces the port did not");
            wrong.AddRange(recorded.Wrong.Select(w => $"gauss {explosions}: {w}"));
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(20)));
        Assert.True(explosions >= 8 && sparks >= 150 && electric >= 200, $"{explosions} explosions, {sparks} ring sparks, {electric} electric particles");
    }
}
